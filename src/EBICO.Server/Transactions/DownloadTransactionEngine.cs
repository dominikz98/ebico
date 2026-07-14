using System.Security.Cryptography;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.Extensions.Options;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Server.Transactions;

/// <summary>
/// Default <see cref="IDownloadTransactionEngine"/>. Composes the reusable Core primitives —
/// <see cref="EbicsCompression"/> (compression), <see cref="EncryptionE002"/> (hybrid encryption for
/// the subscriber's key) and <see cref="EbicsSegmentation"/> (splitting) — into the three-phase
/// download state machine, backed by the <see cref="IDownloadTransactionStore"/> and fed by the
/// <see cref="IDownloadDataProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// The transaction-level and segment-level conditions (unknown transaction id, out-of-range segment
/// numbers, no data available, segment-count limits) are control flow and are returned directly as
/// return codes. The server-to-client data path mirrors the HPB order handler
/// (compress → E002-encrypt for the subscriber → recipient-key digest) and adds segmentation.
/// </para>
/// <para>
/// Data provisioning is <em>consuming</em>: the initialisation dequeues the payload from the provider;
/// a positive receipt leaves it consumed, a negative receipt re-enqueues it so it can be downloaded
/// again (see <c>docs/server/download-transaction.md</c>).
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the download response is not signed (X002 is M4); no order signature is
/// produced. The canonical Initialisation/Transfer/Receipt split, the exact segment semantics and the
/// receipt-code mapping are to be verified against the official EBICS annexes.
/// </para>
/// </remarks>
public sealed class DownloadTransactionEngine : IDownloadTransactionEngine
{
    /// <summary>The H003/H004 generic download order type (file download).</summary>
    public const string FdlOrderType = "FDL";

    /// <summary>The H005 generic download order type (business transaction download).</summary>
    public const string BtdOrderType = "BTD";

    private readonly IDownloadTransactionStore _store;
    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;
    private readonly IDownloadDataProvider _dataProvider;
    private readonly TimeProvider _timeProvider;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the engine.</summary>
    /// <param name="store">The download transaction store.</param>
    /// <param name="masterData">The master-data manager used to resolve the subscriber.</param>
    /// <param name="keyStore">The server key store the subscriber's encryption key is read from.</param>
    /// <param name="dataProvider">The provider supplying the order data to download.</param>
    /// <param name="timeProvider">The clock used to stamp transaction creation.</param>
    /// <param name="options">The server options (segment size/limits).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DownloadTransactionEngine(
        IDownloadTransactionStore store,
        IMasterDataManager masterData,
        IServerKeyStore keyStore,
        IDownloadDataProvider dataProvider,
        TimeProvider timeProvider,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(dataProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _masterData = masterData;
        _keyStore = keyStore;
        _dataProvider = dataProvider;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    /// <summary>
    /// Whether <paramref name="orderType"/> is a generic download order type handled by the engine
    /// (<see cref="FdlOrderType"/> for H003/H004, <see cref="BtdOrderType"/> for H005).
    /// </summary>
    /// <param name="orderType">The extracted order type, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a download order type; otherwise <see langword="false"/>.</returns>
    public static bool IsDownloadOrderType(string? orderType)
        => orderType is FdlOrderType or BtdOrderType;

    /// <inheritdoc />
    public bool OwnsTransaction(byte[]? transactionId)
        => transactionId is not null && _store.TryGet(Convert.ToHexString(transactionId), out _);

    /// <inheritdoc />
    public async Task<DownloadTransactionResult> BeginDownloadAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fields = ExtractInit(context.Envelope);

        if (!HostId.TryCreate(fields.HostId, out var hostId)
            || !PartnerId.TryCreate(fields.PartnerId, out var partnerId)
            || !UserId.TryCreate(fields.UserId, out var userId))
        {
            return Init(EbicsReturnCode.InvalidUserOrUserState);
        }

        // The download requires a fully onboarded subscriber (INI + HIA done).
        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.Ready)
        {
            return Init(EbicsReturnCode.InvalidUserOrUserState);
        }

        // The response is encrypted for the subscriber's encryption key (E002), stored during HIA. A
        // Ready subscriber is expected to have one; a missing key is an inconsistent state.
        var keyRef = new SubscriberKeyRef(hostId, partnerId, userId);
        var subscriberEnc = await _keyStore.GetAsync(keyRef, KeyPurpose.Encryption, ct).ConfigureAwait(false);
        if (subscriberEnc is null)
        {
            return Init(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Provision the order data. Consuming semantics: dequeue now; a negative receipt re-enqueues it.
        var orderType = context.OrderType!;
        var orderData = await _dataProvider
            .TryDequeueAsync(new DownloadDataRequest(context.Version, keyRef, orderType), ct)
            .ConfigureAwait(false);
        if (orderData is null)
        {
            return Init(EbicsReturnCode.NoDownloadDataAvailable);
        }

        // Server-to-client data path (mirrors HpbOrderHandlerBase): compress, E002-encrypt for the
        // subscriber, compute the recipient-key digest, then segment the ciphertext.
        var compressed = EbicsCompression.Compress(orderData);
        var encrypted = EncryptionE002.Encrypt(compressed, subscriberEnc.Key, subscriberEnc.Version);
        var digest = PublicKeyFingerprint.Compute(subscriberEnc.Key);
        var segmented = EbicsSegmentation.Split(encrypted.EncryptedOrderDataBytes, _options.SegmentSizeBytes);

        if (segmented.NumSegments > _options.MaxDownloadSegments)
        {
            // Cannot serve this payload within the configured ceiling; return it to the queue so it is
            // not lost, then report the limit.
            await _dataProvider.EnqueueAsync(keyRef, orderType, orderData, ct).ConfigureAwait(false);
            return Init(EbicsReturnCode.MaxSegmentsExceeded);
        }

        var transactionId = RandomNumberGenerator.GetBytes(16);
        var transaction = new DownloadTransaction(
            transactionId,
            context.Version,
            keyRef,
            orderType,
            segmented.Segments,
            encrypted.EncryptedTransactionKey,
            digest,
            subscriberEnc.Version,
            orderData,
            _timeProvider.GetUtcNow());

        _store.Create(transaction);

        // The initialisation response carries segment 1 together with the DataEncryptionInfo.
        var firstSegment = new DownloadSegmentPayload(
            transaction.GetSegment(1),
            encrypted.EncryptedTransactionKey,
            digest,
            subscriberEnc.Version);

        return new DownloadTransactionResult(
            EbicsReturnCode.Ok,
            EbicsTransactionPhase.Initialisation,
            transactionId,
            (ulong)segmented.NumSegments,
            SegmentNumber: 1,
            LastSegment: segmented.NumSegments == 1,
            Segment: firstSegment);
    }

    /// <inheritdoc />
    public Task<DownloadTransactionResult> ContinueDownloadAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fields = ExtractTransfer(context.Envelope);

        if (fields.TransactionId is not { } transactionId)
        {
            return Task.FromResult(Transfer(EbicsReturnCode.TxUnknownTxid, null, fields.SegmentNumber, fields.LastSegment));
        }

        if (!_store.TryGet(Convert.ToHexString(transactionId), out var transaction) || transaction is null)
        {
            return Task.FromResult(Transfer(EbicsReturnCode.TxUnknownTxid, transactionId, fields.SegmentNumber, fields.LastSegment));
        }

        // Segments are 1-based; a missing number or a number beyond the announced count is a protocol error.
        if (fields.SegmentNumber is not { } segmentNumber || segmentNumber == 0)
        {
            return Task.FromResult(Transfer(EbicsReturnCode.InvalidRequestContent, transactionId, fields.SegmentNumber, fields.LastSegment));
        }

        if (segmentNumber > (ulong)transaction.NumSegments)
        {
            return Task.FromResult(Transfer(EbicsReturnCode.TxSegmentNumberExceeded, transactionId, segmentNumber, fields.LastSegment));
        }

        var lastSegment = segmentNumber == (ulong)transaction.NumSegments;
        var payload = new DownloadSegmentPayload(transaction.GetSegment((int)segmentNumber));

        return Task.FromResult(new DownloadTransactionResult(
            EbicsReturnCode.Ok,
            EbicsTransactionPhase.Transfer,
            transactionId,
            SegmentNumber: segmentNumber,
            LastSegment: lastSegment,
            Segment: payload));
    }

    /// <inheritdoc />
    public async Task<DownloadTransactionResult> AcknowledgeReceiptAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fields = ExtractReceipt(context.Envelope);

        if (fields.TransactionId is not { } transactionId)
        {
            return Receipt(EbicsReturnCode.TxUnknownTxid, null);
        }

        if (!_store.TryGet(Convert.ToHexString(transactionId), out var transaction) || transaction is null)
        {
            return Receipt(EbicsReturnCode.TxUnknownTxid, transactionId);
        }

        // A receipt without a receipt code is a malformed request.
        if (fields.ReceiptCode is not { } receiptCode)
        {
            return Receipt(EbicsReturnCode.InvalidRequestContent, transactionId);
        }

        // The download is over regardless of the outcome; drop the transaction.
        _store.Remove(transaction.TransactionIdHex);

        // Positive receipt (0): post-processing done, the data stays consumed. Negative receipt (non-0):
        // post-processing skipped, re-enqueue the data so it can be downloaded again.
        if (receiptCode == 0)
        {
            return Receipt(EbicsReturnCode.DownloadPostprocessDone, transactionId);
        }

        await _dataProvider
            .EnqueueAsync(transaction.Subscriber, transaction.OrderType, transaction.OrderDataPlaintext, ct)
            .ConfigureAwait(false);
        return Receipt(EbicsReturnCode.DownloadPostprocessSkipped, transactionId);
    }

    private static DownloadTransactionResult Init(EbicsReturnCode returnCode)
        => new(returnCode, EbicsTransactionPhase.Initialisation);

    private static DownloadTransactionResult Transfer(EbicsReturnCode returnCode, byte[]? transactionId, ulong? segmentNumber, bool lastSegment)
        => new(returnCode, EbicsTransactionPhase.Transfer, transactionId, SegmentNumber: segmentNumber, LastSegment: lastSegment);

    private static DownloadTransactionResult Receipt(EbicsReturnCode returnCode, byte[]? transactionId)
        => new(returnCode, EbicsTransactionPhase.Receipt, transactionId);

    // --- Version-neutral field extraction (mirrors UploadTransactionEngine) --------------------------

    private readonly record struct InitFields(string? HostId, string? PartnerId, string? UserId);

    private readonly record struct TransferFields(byte[]? TransactionId, ulong? SegmentNumber, bool LastSegment);

    private readonly record struct ReceiptFields(byte[]? TransactionId, byte? ReceiptCode);

    private static InitFields ExtractInit(IEbicsRequestEnvelope envelope) => envelope switch
    {
        H003.EbicsRequest r => new InitFields(r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
        H004.EbicsRequest r => new InitFields(r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
        H005.EbicsRequest r => new InitFields(r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
        _ => default,
    };

    private static TransferFields ExtractTransfer(IEbicsRequestEnvelope envelope) => envelope switch
    {
        H003.EbicsRequest r => new TransferFields(
            r.Header?.Static?.TransactionId,
            r.Header?.Mutable?.SegmentNumber?.Value,
            r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false),
        H004.EbicsRequest r => new TransferFields(
            r.Header?.Static?.TransactionId,
            r.Header?.Mutable?.SegmentNumber?.Value,
            r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false),
        H005.EbicsRequest r => new TransferFields(
            r.Header?.Static?.TransactionId,
            r.Header?.Mutable?.SegmentNumber?.Value,
            r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false),
        _ => default,
    };

    private static ReceiptFields ExtractReceipt(IEbicsRequestEnvelope envelope) => envelope switch
    {
        H003.EbicsRequest r => new ReceiptFields(r.Header?.Static?.TransactionId, r.Body?.TransferReceipt?.ReceiptCode),
        H004.EbicsRequest r => new ReceiptFields(r.Header?.Static?.TransactionId, r.Body?.TransferReceipt?.ReceiptCode),
        H005.EbicsRequest r => new ReceiptFields(r.Header?.Static?.TransactionId, r.Body?.TransferReceipt?.ReceiptCode),
        _ => default,
    };
}
