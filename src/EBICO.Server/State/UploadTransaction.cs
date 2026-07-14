using EBICO.Core;
using EBICO.Server.Transactions;

namespace EBICO.Server.State;

/// <summary>
/// The outcome of buffering one transfer-phase order-data segment into an
/// <see cref="UploadTransaction"/>.
/// </summary>
public enum SegmentAppendStatus
{
    /// <summary>The segment was buffered; the transaction is not yet complete.</summary>
    Buffered,

    /// <summary>The segment carried <c>lastSegment</c> and completed the transaction — all segments are present.</summary>
    Ready,

    /// <summary>A segment with this number was already buffered (a replay of a transaction step).</summary>
    Duplicate,

    /// <summary>The segment carried <c>lastSegment</c> but fewer segments than announced have arrived.</summary>
    Underrun,
}

/// <summary>
/// The result of <see cref="UploadTransaction.AppendSegment"/>: the buffering
/// <see cref="SegmentAppendStatus"/> and, when the status is <see cref="SegmentAppendStatus.Ready"/>,
/// the ordered segment snapshot ready for reassembly.
/// </summary>
/// <param name="Status">The buffering outcome.</param>
/// <param name="OrderedSegments">The ordered segments when <see cref="Status"/> is <see cref="SegmentAppendStatus.Ready"/>; otherwise <see langword="null"/>.</param>
public readonly record struct SegmentAppendResult(SegmentAppendStatus Status, IReadOnlyList<byte[]>? OrderedSegments);

/// <summary>
/// The in-flight state of a single server-side EBICS upload transaction (issue #32): everything the
/// server captures during the <b>Initialisation</b> phase and needs to reassemble the order data as
/// the <b>Transfer</b>-phase segments arrive. Keyed by the 16-byte transaction id the server assigns.
/// </summary>
/// <remarks>
/// <para>
/// The transaction key is stored <em>decrypted</em> (the AES-128 order-data key), obtained once during
/// initialisation by RSA-decrypting <c>DataEncryptionInfo/TransactionKey</c> with the bank's private
/// encryption key. The transfer segments carry chunks of the AES ciphertext; on the last segment the
/// engine reassembles them, decrypts with <see cref="TransactionKey"/> and decompresses.
/// </para>
/// <para>
/// The subscriber identifiers (<see cref="Subscriber"/>) are captured here because the transfer-phase
/// requests carry only the transaction id, not the full static header. Segment buffering is guarded by
/// an internal lock so concurrent transfers for the same transaction stay consistent.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the raw <see cref="SignatureData"/> (the order signature / ES) is retained
/// but <em>not</em> cryptographically verified in this issue (consistent with the single-phase key
/// handlers); the electronic-signature verification is a follow-up. Idle expiry (issue #35) is driven by
/// <see cref="LastActivityAt"/>/<see cref="IsExpired"/>; the store evicts expired and orphaned
/// transactions (lazily on access and via the background cleanup service).
/// </para>
/// </remarks>
public sealed class UploadTransaction
{
    private readonly object _gate = new();
    private readonly SortedDictionary<int, byte[]> _segments = [];
    private long _lastActivityTicks;

    /// <summary>Initializes a new upload transaction captured during the initialisation phase.</summary>
    /// <param name="transactionId">The 16-byte transaction id assigned by the server.</param>
    /// <param name="version">The protocol version the transaction runs under.</param>
    /// <param name="subscriber">The subscriber the upload belongs to (captured from the initialisation header).</param>
    /// <param name="orderType">The upload order type (e.g. <c>"FUL"</c>/<c>"BTU"</c>).</param>
    /// <param name="numSegments">The total number of segments announced for the transaction (&#8805; 1).</param>
    /// <param name="transactionKey">The decrypted AES-128 transaction key used to decrypt the reassembled order data.</param>
    /// <param name="signatureData">The raw order-signature (ES) blob from the initialisation, retained for later verification; may be <see langword="null"/>.</param>
    /// <param name="createdAt">The time the transaction was created.</param>
    /// <param name="effectiveOrderType">The resolved classical order-type code (e.g. <c>"CCT"</c>) the transfer-phase order processing dispatches on; captured at initialisation because the transfer requests carry no order type. May be <see langword="null"/> when the raw <paramref name="orderType"/> does not resolve to a business order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transactionId"/>, <paramref name="orderType"/> or <paramref name="transactionKey"/> is <see langword="null"/>.</exception>
    public UploadTransaction(
        byte[] transactionId,
        EbicsVersion version,
        SubscriberKeyRef subscriber,
        string orderType,
        int numSegments,
        byte[] transactionKey,
        byte[]? signatureData,
        DateTimeOffset createdAt,
        string? effectiveOrderType = null)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        ArgumentNullException.ThrowIfNull(orderType);
        ArgumentNullException.ThrowIfNull(transactionKey);

        TransactionId = transactionId;
        TransactionIdHex = Convert.ToHexString(transactionId);
        Version = version;
        Subscriber = subscriber;
        OrderType = orderType;
        EffectiveOrderType = effectiveOrderType;
        NumSegments = numSegments;
        TransactionKey = transactionKey;
        SignatureData = signatureData;
        CreatedAt = createdAt;
        _lastActivityTicks = createdAt.UtcTicks;
    }

    /// <summary>The 16-byte transaction id assigned by the server.</summary>
    public byte[] TransactionId { get; }

    /// <summary>The upper-case hex form of <see cref="TransactionId"/>; the key used by the transaction store.</summary>
    public string TransactionIdHex { get; }

    /// <summary>The protocol version the transaction runs under.</summary>
    public EbicsVersion Version { get; }

    /// <summary>The subscriber the upload belongs to (captured during initialisation).</summary>
    public SubscriberKeyRef Subscriber { get; }

    /// <summary>The upload order type (e.g. <c>"FUL"</c>/<c>"BTU"</c>).</summary>
    public string OrderType { get; }

    /// <summary>
    /// The resolved classical order-type code (e.g. <c>"CCT"</c>/<c>"CDD"</c>) captured during
    /// initialisation, or <see langword="null"/> when the raw <see cref="OrderType"/> did not resolve to
    /// a business order. Used by the transfer-phase order processing, which no longer sees the order type.
    /// </summary>
    public string? EffectiveOrderType { get; }

    /// <summary>The total number of segments announced for the transaction.</summary>
    public int NumSegments { get; }

    /// <summary>The decrypted AES-128 transaction key that decrypts the reassembled order data.</summary>
    public byte[] TransactionKey { get; }

    /// <summary>The raw order-signature (ES) blob retained from the initialisation, or <see langword="null"/>. Not verified in this issue.</summary>
    public byte[]? SignatureData { get; }

    /// <summary>The time the transaction was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The time of the last activity on this transaction (its creation, then each accepted transfer
    /// step). The idle-expiry window (<see cref="IsExpired"/>) slides on this value, so a long
    /// multi-segment transfer does not expire mid-flight. Read atomically; safe for concurrent access.
    /// </summary>
    public DateTimeOffset LastActivityAt => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

    /// <summary>Records activity on the transaction, sliding the idle-expiry window to <paramref name="now"/>.</summary>
    /// <param name="now">The current time.</param>
    public void Touch(DateTimeOffset now) => Interlocked.Exchange(ref _lastActivityTicks, now.UtcTicks);

    /// <summary>
    /// Whether the transaction has been idle for at least <paramref name="timeout"/> as of
    /// <paramref name="now"/>. A non-positive <paramref name="timeout"/> disables expiry (always
    /// <see langword="false"/>).
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="timeout">The idle timeout; <see cref="TimeSpan.Zero"/> or less disables expiry.</param>
    /// <returns><see langword="true"/> when the transaction has expired; otherwise <see langword="false"/>.</returns>
    public bool IsExpired(DateTimeOffset now, TimeSpan timeout)
        => timeout > TimeSpan.Zero && now.UtcTicks - Interlocked.Read(ref _lastActivityTicks) >= timeout.Ticks;

    /// <summary>Whether every announced segment has been received and the order data reassembled.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    /// The reassembled, decrypted and decompressed order data once the transaction is complete;
    /// <see langword="null"/> while segments are still in flight.
    /// </summary>
    public byte[]? OrderData { get; private set; }

    /// <summary>
    /// Buffers one transfer-phase segment. Assumes <paramref name="segmentNumber"/> has already been
    /// range-checked against <see cref="NumSegments"/> by the caller. Duplicate detection, the
    /// completeness decision and the ordered snapshot are performed atomically under the transaction
    /// lock.
    /// </summary>
    /// <param name="segmentNumber">The 1-based segment number (already validated to be in <c>[1, NumSegments]</c>).</param>
    /// <param name="data">The raw (base64-decoded) order-data bytes of the segment.</param>
    /// <param name="lastSegment">Whether the client flagged this as the last segment of the transaction.</param>
    /// <returns>The buffering outcome; the ordered segments when the transaction became complete.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public SegmentAppendResult AppendSegment(int segmentNumber, byte[] data, bool lastSegment)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_gate)
        {
            if (!_segments.TryAdd(segmentNumber, data))
            {
                return new SegmentAppendResult(SegmentAppendStatus.Duplicate, null);
            }

            if (!lastSegment)
            {
                return new SegmentAppendResult(SegmentAppendStatus.Buffered, null);
            }

            // lastSegment: the transaction is complete only when every announced segment has arrived.
            if (_segments.Count != NumSegments)
            {
                return new SegmentAppendResult(SegmentAppendStatus.Underrun, null);
            }

            return new SegmentAppendResult(SegmentAppendStatus.Ready, [.. _segments.Values]);
        }
    }

    /// <summary>Records the reassembled, decrypted and decompressed order data and marks the transaction complete.</summary>
    /// <param name="orderData">The decoded order data.</param>
    /// <exception cref="ArgumentNullException"><paramref name="orderData"/> is <see langword="null"/>.</exception>
    public void Complete(byte[] orderData)
    {
        ArgumentNullException.ThrowIfNull(orderData);

        lock (_gate)
        {
            OrderData = orderData;
            IsComplete = true;
        }
    }
}
