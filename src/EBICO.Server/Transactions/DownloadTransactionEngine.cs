using System.Security.Cryptography;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Orders;
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
public sealed class DownloadTransactionEngine : IDownloadTransactionEngine, ITransactionEvictor
{
    /// <summary>The H003/H004 generic download order type (file download).</summary>
    public const string FdlOrderType = "FDL";

    /// <summary>The H005 generic download order type (business transaction download).</summary>
    public const string BtdOrderType = "BTD";

    private readonly IDownloadTransactionStore _store;
    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;
    private readonly IDownloadDataProvider _dataProvider;
    private readonly IDownloadOrderProcessor _downloadOrderProcessor;
    private readonly IEventLog _eventLog;
    private readonly TimeProvider _timeProvider;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the engine.</summary>
    /// <param name="store">The download transaction store.</param>
    /// <param name="masterData">The master-data manager used to resolve the subscriber.</param>
    /// <param name="keyStore">The server key store the subscriber's encryption key is read from.</param>
    /// <param name="dataProvider">The provider supplying the order data to download.</param>
    /// <param name="downloadOrderProcessor">The on-demand content generator invoked when no payload is pre-seeded for a resolved statement order type (issue #40).</param>
    /// <param name="eventLog">The append-only event log (issue #69) the download lifecycle events are written to.</param>
    /// <param name="timeProvider">The clock used to stamp transaction creation.</param>
    /// <param name="options">The server options (segment size/limits).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DownloadTransactionEngine(
        IDownloadTransactionStore store,
        IMasterDataManager masterData,
        IServerKeyStore keyStore,
        IDownloadDataProvider dataProvider,
        IDownloadOrderProcessor downloadOrderProcessor,
        IEventLog eventLog,
        TimeProvider timeProvider,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(dataProvider);
        ArgumentNullException.ThrowIfNull(downloadOrderProcessor);
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _masterData = masterData;
        _keyStore = keyStore;
        _dataProvider = dataProvider;
        _downloadOrderProcessor = downloadOrderProcessor;
        _eventLog = eventLog;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    /// <summary>
    /// Whether <paramref name="orderType"/> is a download order type handled by the engine: the generic
    /// <see cref="FdlOrderType"/> (H003/H004) or <see cref="BtdOrderType"/> (H005), or a classical
    /// statement/report order type submitted directly (STA/VMK/C53/C52/C54, see
    /// <see cref="BtfOrderTypeCatalog.IsDownloadOrderType"/>).
    /// </summary>
    /// <param name="orderType">The extracted order type, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a download order type; otherwise <see langword="false"/>.</returns>
    public static bool IsDownloadOrderType(string? orderType)
        => orderType is FdlOrderType or BtdOrderType || BtfOrderTypeCatalog.IsDownloadOrderType(orderType);

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

        // Authorisation per BTF/order type (issue #38): the subscriber must hold a permission for the
        // requested order type. Checked before dequeuing so an unauthorised download consumes no data.
        // The effective (classical) order type is resolved across the three conventions: the H005 BTF
        // (BTDOrderParams/Service), an H003/H004 FDL + FileFormat, or a classical code submitted directly
        // (issue #40). It is used both here and as the queue/generation key below.
        var effectiveOrderType = BtfOrderTypeCatalog.ResolveDownloadOrderType(context.OrderType, context.Btf, fields.FileFormat);
        if (effectiveOrderType is null || !subscriber.HasPermissionFor(effectiveOrderType))
        {
            return Init(EbicsReturnCode.AuthorisationOrderTypeFailed);
        }

        // The response is encrypted for the subscriber's encryption key (E002), stored during HIA. A
        // Ready subscriber is expected to have one; a missing key is an inconsistent state.
        var keyRef = new SubscriberKeyRef(hostId, partnerId, userId);
        var subscriberEnc = await _keyStore.GetAsync(keyRef, KeyPurpose.Encryption, ct).ConfigureAwait(false);
        if (subscriberEnc is null)
        {
            return Init(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Soft ceiling on concurrent transactions (0 = unlimited). Checked before dequeuing so a rejected
        // initialisation does not consume order data. The count-then-create is not atomic (acceptable).
        if (_options.MaxConcurrentTransactions > 0 && _store.Count >= _options.MaxConcurrentTransactions)
        {
            return Init(EbicsReturnCode.MaxTransactionsExceeded);
        }

        // Provision the order data. Consuming semantics: dequeue now; a negative receipt re-enqueues it.
        // Precedence: (1) pre-seeded data under the resolved order type; (2) a backward-compat probe under
        // the raw FDL/BTD key (what the engine keyed on before #40, so a queue seeded under "FDL"/"BTD"
        // keeps working); (3) on-demand generation for a statement/report order type. The key actually hit
        // is remembered so a re-enqueue (negative receipt / eviction) lands back under the same key.
        var queueKey = effectiveOrderType;
        var orderData = await _dataProvider
            .TryDequeueAsync(new DownloadDataRequest(context.Version, keyRef, queueKey), ct)
            .ConfigureAwait(false);

        if (orderData is null
            && context.OrderType is { } rawOrderType
            && !string.Equals(effectiveOrderType, rawOrderType, StringComparison.Ordinal))
        {
            queueKey = rawOrderType;
            orderData = await _dataProvider
                .TryDequeueAsync(new DownloadDataRequest(context.Version, keyRef, queueKey), ct)
                .ConfigureAwait(false);
        }

        if (orderData is null && _downloadOrderProcessor.CanProcess(effectiveOrderType))
        {
            queueKey = effectiveOrderType;
            orderData = await _downloadOrderProcessor
                .GenerateAsync(new DownloadOrderRequest(keyRef, context.Version, effectiveOrderType, fields.DateRange), ct)
                .ConfigureAwait(false);
        }

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
            await _dataProvider.EnqueueAsync(keyRef, queueKey, orderData, ct).ConfigureAwait(false);
            return Init(EbicsReturnCode.MaxSegmentsExceeded);
        }

        var transactionId = RandomNumberGenerator.GetBytes(16);
        var transaction = new DownloadTransaction(
            transactionId,
            context.Version,
            keyRef,
            queueKey,
            segmented.Segments,
            encrypted.EncryptedTransactionKey,
            digest,
            subscriberEnc.Version,
            orderData,
            _timeProvider.GetUtcNow());

        _store.Create(transaction);

        await AppendEventAsync(
            transaction,
            EbicsEventType.DownloadStarted,
            EbicsEventSeverity.Info,
            EbicsEventVisibility.CustomerVisible,
            EbicsReturnCode.Ok,
            $"Download started ({segmented.NumSegments} segment(s), order type {queueKey}).",
            ct).ConfigureAwait(false);

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
    public async Task<DownloadTransactionResult> ContinueDownloadAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fields = ExtractTransfer(context.Envelope);

        if (fields.TransactionId is not { } transactionId)
        {
            return Transfer(EbicsReturnCode.TxUnknownTxid, null, fields.SegmentNumber, fields.LastSegment);
        }

        if (!_store.TryGet(Convert.ToHexString(transactionId), out var transaction) || transaction is null)
        {
            return Transfer(EbicsReturnCode.TxUnknownTxid, transactionId, fields.SegmentNumber, fields.LastSegment);
        }

        // Lazy expiry: an idle-timed-out transaction is evicted (re-enqueuing its already-dequeued order
        // data exactly once) and answered as an unknown id (091101). A live one has its idle window slid.
        var now = _timeProvider.GetUtcNow();
        if (transaction.IsExpired(now, _options.TransactionTimeout))
        {
            await EvictAsync(transaction, ct).ConfigureAwait(false);
            return Transfer(EbicsReturnCode.TxUnknownTxid, transactionId, fields.SegmentNumber, fields.LastSegment);
        }

        transaction.Touch(now);

        // Segments are 1-based; a missing number or a number beyond the announced count is a protocol error.
        if (fields.SegmentNumber is not { } segmentNumber || segmentNumber == 0)
        {
            return Transfer(EbicsReturnCode.InvalidRequestContent, transactionId, fields.SegmentNumber, fields.LastSegment);
        }

        if (segmentNumber > (ulong)transaction.NumSegments)
        {
            return Transfer(EbicsReturnCode.TxSegmentNumberExceeded, transactionId, segmentNumber, fields.LastSegment);
        }

        var lastSegment = segmentNumber == (ulong)transaction.NumSegments;
        var payload = new DownloadSegmentPayload(transaction.GetSegment((int)segmentNumber));

        return new DownloadTransactionResult(
            EbicsReturnCode.Ok,
            EbicsTransactionPhase.Transfer,
            transactionId,
            SegmentNumber: segmentNumber,
            LastSegment: lastSegment,
            Segment: payload);
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
            await AppendEventAsync(
                transaction,
                EbicsEventType.DownloadCompleted,
                EbicsEventSeverity.Info,
                EbicsEventVisibility.CustomerVisible,
                EbicsReturnCode.DownloadPostprocessDone,
                $"Download completed with a positive receipt (order type {transaction.OrderType}).",
                ct).ConfigureAwait(false);
            return Receipt(EbicsReturnCode.DownloadPostprocessDone, transactionId);
        }

        await _dataProvider
            .EnqueueAsync(transaction.Subscriber, transaction.OrderType, transaction.OrderDataPlaintext, ct)
            .ConfigureAwait(false);

        await AppendEventAsync(
            transaction,
            EbicsEventType.ReceiptNegative,
            EbicsEventSeverity.Warning,
            EbicsEventVisibility.CustomerVisible,
            EbicsReturnCode.DownloadPostprocessSkipped,
            $"Download acknowledged with a negative receipt; data re-enqueued (order type {transaction.OrderType}).",
            ct).ConfigureAwait(false);
        return Receipt(EbicsReturnCode.DownloadPostprocessSkipped, transactionId);
    }

    /// <inheritdoc />
    public async Task<int> EvictExpiredAsync(CancellationToken ct = default)
    {
        var timeout = _options.TransactionTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow();
        var evicted = 0;
        foreach (var transaction in _store.GetAll())
        {
            ct.ThrowIfCancellationRequested();
            if (transaction.IsExpired(now, timeout) && await EvictAsync(transaction, ct).ConfigureAwait(false))
            {
                await AppendEventAsync(
                    transaction,
                    EbicsEventType.TransactionEvicted,
                    EbicsEventSeverity.Warning,
                    EbicsEventVisibility.Internal,
                    returnCode: null,
                    $"Download transaction evicted after idle timeout; data re-enqueued (order type {transaction.OrderType}).",
                    ct).ConfigureAwait(false);
                evicted++;
            }
        }

        return evicted;
    }

    // Writes a transaction lifecycle event (issue #69) carrying the transaction's full subscriber triple,
    // order type and hex id. Complements the pipeline's per-request event with business-level semantics.
    private Task AppendEventAsync(
        DownloadTransaction transaction,
        EbicsEventType type,
        EbicsEventSeverity severity,
        EbicsEventVisibility visibility,
        EbicsReturnCode? returnCode,
        string message,
        CancellationToken ct)
        => _eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = type,
                Severity = severity,
                Visibility = visibility,
                HostId = transaction.Subscriber.HostId,
                PartnerId = transaction.Subscriber.PartnerId,
                UserId = transaction.Subscriber.UserId,
                OrderType = transaction.OrderType,
                TransactionId = transaction.TransactionIdHex,
                ReturnCode = returnCode,
                Message = message,
            },
            ct);

    // Evicts one transaction and re-enqueues its already-dequeued order data. The Remove return value is
    // the "exactly once" guard: whoever wins the race (lazy path vs. sweeper) re-enqueues, the loser does
    // not, so the data lands back in the queue exactly once.
    private async Task<bool> EvictAsync(DownloadTransaction transaction, CancellationToken ct)
    {
        if (!_store.Remove(transaction.TransactionIdHex))
        {
            return false;
        }

        await _dataProvider
            .EnqueueAsync(transaction.Subscriber, transaction.OrderType, transaction.OrderDataPlaintext, ct)
            .ConfigureAwait(false);
        return true;
    }

    private static DownloadTransactionResult Init(EbicsReturnCode returnCode)
        => new(returnCode, EbicsTransactionPhase.Initialisation);

    private static DownloadTransactionResult Transfer(EbicsReturnCode returnCode, byte[]? transactionId, ulong? segmentNumber, bool lastSegment)
        => new(returnCode, EbicsTransactionPhase.Transfer, transactionId, SegmentNumber: segmentNumber, LastSegment: lastSegment);

    private static DownloadTransactionResult Receipt(EbicsReturnCode returnCode, byte[]? transactionId)
        => new(returnCode, EbicsTransactionPhase.Receipt, transactionId);

    // --- Version-neutral field extraction (mirrors UploadTransactionEngine) --------------------------

    private readonly record struct InitFields(
        string? HostId, string? PartnerId, string? UserId, string? FileFormat, DateRange? DateRange);

    private readonly record struct TransferFields(byte[]? TransactionId, ulong? SegmentNumber, bool LastSegment);

    private readonly record struct ReceiptFields(byte[]? TransactionId, byte? ReceiptCode);

    private static InitFields ExtractInit(IEbicsRequestEnvelope envelope) => envelope switch
    {
        H003.EbicsRequest r => new InitFields(
            r.Header?.Static?.HostId,
            r.Header?.Static?.PartnerId,
            r.Header?.Static?.UserId,
            (r.Header?.Static?.OrderDetails?.OrderParams as H003.FdlOrderParamsType)?.FileFormat?.Value,
            ExtractDateRange(r.Header?.Static?.OrderDetails?.OrderParams)),
        H004.EbicsRequest r => new InitFields(
            r.Header?.Static?.HostId,
            r.Header?.Static?.PartnerId,
            r.Header?.Static?.UserId,
            (r.Header?.Static?.OrderDetails?.OrderParams as H004.FdlOrderParamsType)?.FileFormat?.Value,
            ExtractDateRange(r.Header?.Static?.OrderDetails?.OrderParams)),
        H005.EbicsRequest r => new InitFields(
            r.Header?.Static?.HostId,
            r.Header?.Static?.PartnerId,
            r.Header?.Static?.UserId,
            null,
            ExtractDateRange(r.Header?.Static?.OrderDetails?.OrderParams)),
        _ => default,
    };

    // Extracts the reporting period from the version-specific order-params element (FDLOrderParams or
    // StandardOrderParams for H003/H004, BTDOrderParams for H005). Returns null when no DateRange is present.
    private static DateRange? ExtractDateRange(object? orderParams) => orderParams switch
    {
        H003.FdlOrderParamsType { DateRange: { } d } => new DateRange(DateOnly.FromDateTime(d.Start), DateOnly.FromDateTime(d.End)),
        H003.StandardOrderParamsType { DateRange: { } d } => new DateRange(DateOnly.FromDateTime(d.Start), DateOnly.FromDateTime(d.End)),
        H004.FdlOrderParamsType { DateRange: { } d } => new DateRange(DateOnly.FromDateTime(d.Start), DateOnly.FromDateTime(d.End)),
        H004.StandardOrderParamsType { DateRange: { } d } => new DateRange(DateOnly.FromDateTime(d.Start), DateOnly.FromDateTime(d.End)),
        H005.BtfParamsTyp { DateRange: { } d } => new DateRange(DateOnly.FromDateTime(d.Start), DateOnly.FromDateTime(d.End)),
        _ => null,
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
