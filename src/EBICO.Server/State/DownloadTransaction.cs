using EBICO.Core;
using EBICO.Core.Crypto;

namespace EBICO.Server.State;

/// <summary>
/// The in-flight state of a single server-side EBICS download transaction (issue #33): everything the
/// server prepares during the <b>Initialisation</b> phase and needs to serve the <b>Transfer</b>-phase
/// segments and process the <b>Receipt</b>. Keyed by the 16-byte transaction id the server assigns.
/// </summary>
/// <remarks>
/// <para>
/// Unlike an upload, the whole payload is known up front: during initialisation the server fetches the
/// order data, compresses and E002-encrypts it for the subscriber, and splits the ciphertext into
/// <see cref="Segments"/>. The transfer phase then hands out those segments one message at a time; no
/// buffering or reassembly is needed on the server side. The initialisation response also carries the
/// encrypted transaction key and the recipient-key digest (<see cref="EncryptedTransactionKey"/> /
/// <see cref="EncryptionPubKeyDigest"/> / <see cref="EncryptionVersion"/>), so only the subscriber can
/// decrypt the payload.
/// </para>
/// <para>
/// <see cref="OrderDataPlaintext"/> is retained so a <em>negative</em> receipt can re-enqueue the order
/// data with the <see cref="IDownloadDataProvider"/> (the download is considered not delivered); a
/// <em>positive</em> receipt consumes it (the initialisation already dequeued it).
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the download response is not signed (X002 is M4) and no order signature is
/// produced. Orphaned transactions (client abandons after initialisation) are not evicted here — a
/// TTL/recovery model is issue #35.
/// </para>
/// </remarks>
public sealed class DownloadTransaction
{
    private readonly IReadOnlyList<byte[]> _segments;

    /// <summary>Initializes a new download transaction prepared during the initialisation phase.</summary>
    /// <param name="transactionId">The 16-byte transaction id assigned by the server.</param>
    /// <param name="version">The protocol version the transaction runs under.</param>
    /// <param name="subscriber">The subscriber the download belongs to (captured from the initialisation header).</param>
    /// <param name="orderType">The download order type (<c>"FDL"</c> for H003/H004, <c>"BTD"</c> for H005).</param>
    /// <param name="segments">The E002-encrypted, segmented ciphertext to serve (at least one segment).</param>
    /// <param name="encryptedTransactionKey">The RSA-OAEP-encrypted transaction key for the initialisation response.</param>
    /// <param name="encryptionPubKeyDigest">The SHA-256 digest of the subscriber's encryption key for the initialisation response.</param>
    /// <param name="encryptionVersion">The subscriber encryption key version (e.g. <c>E002</c>).</param>
    /// <param name="orderDataPlaintext">The original (uncompressed, unencrypted) order data, retained for re-enqueue on a negative receipt.</param>
    /// <param name="createdAt">The time the transaction was created.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> is empty.</exception>
    public DownloadTransaction(
        byte[] transactionId,
        EbicsVersion version,
        SubscriberKeyRef subscriber,
        string orderType,
        IReadOnlyList<byte[]> segments,
        byte[] encryptedTransactionKey,
        byte[] encryptionPubKeyDigest,
        KeyVersion encryptionVersion,
        byte[] orderDataPlaintext,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        ArgumentNullException.ThrowIfNull(orderType);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(encryptedTransactionKey);
        ArgumentNullException.ThrowIfNull(encryptionPubKeyDigest);
        ArgumentNullException.ThrowIfNull(orderDataPlaintext);
        if (segments.Count == 0)
        {
            throw new ArgumentException("A download transaction must have at least one segment.", nameof(segments));
        }

        TransactionId = transactionId;
        TransactionIdHex = Convert.ToHexString(transactionId);
        Version = version;
        Subscriber = subscriber;
        OrderType = orderType;
        _segments = segments;
        EncryptedTransactionKey = encryptedTransactionKey;
        EncryptionPubKeyDigest = encryptionPubKeyDigest;
        EncryptionVersion = encryptionVersion;
        OrderDataPlaintext = orderDataPlaintext;
        CreatedAt = createdAt;
    }

    /// <summary>The 16-byte transaction id assigned by the server.</summary>
    public byte[] TransactionId { get; }

    /// <summary>The upper-case hex form of <see cref="TransactionId"/>; the key used by the transaction store.</summary>
    public string TransactionIdHex { get; }

    /// <summary>The protocol version the transaction runs under.</summary>
    public EbicsVersion Version { get; }

    /// <summary>The subscriber the download belongs to (captured during initialisation).</summary>
    public SubscriberKeyRef Subscriber { get; }

    /// <summary>The download order type (<c>"FDL"</c>/<c>"BTD"</c>).</summary>
    public string OrderType { get; }

    /// <summary>The total number of segments the order data was split into (always &#8805; 1).</summary>
    public int NumSegments => _segments.Count;

    /// <summary>The RSA-OAEP-encrypted transaction key emitted in the initialisation response.</summary>
    public byte[] EncryptedTransactionKey { get; }

    /// <summary>The SHA-256 digest of the subscriber's encryption key emitted in the initialisation response.</summary>
    public byte[] EncryptionPubKeyDigest { get; }

    /// <summary>The subscriber encryption key version (e.g. <c>E002</c>).</summary>
    public KeyVersion EncryptionVersion { get; }

    /// <summary>The original order data, retained so a negative receipt can re-enqueue it with the provider.</summary>
    public byte[] OrderDataPlaintext { get; }

    /// <summary>The time the transaction was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Returns the encrypted ciphertext of the given 1-based segment.</summary>
    /// <param name="segmentNumber">The 1-based segment number in <c>[1, <see cref="NumSegments"/>]</c>.</param>
    /// <returns>The segment's raw (pre-base64) ciphertext bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segmentNumber"/> is outside <c>[1, NumSegments]</c>.</exception>
    public byte[] GetSegment(int segmentNumber)
    {
        if (segmentNumber < 1 || segmentNumber > _segments.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segmentNumber), segmentNumber, $"Segment number must be in [1, {_segments.Count}].");
        }

        return _segments[segmentNumber - 1];
    }
}
