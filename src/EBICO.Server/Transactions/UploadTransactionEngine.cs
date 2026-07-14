using System.Security.Cryptography;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Handlers;
using EBICO.Server.Orders;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.Extensions.Options;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Server.Transactions;

/// <summary>
/// Default <see cref="IUploadTransactionEngine"/>. Composes the reusable Core primitives —
/// <see cref="EncryptionE002"/> (transaction-key / order-data decryption), <see cref="EbicsSegmentation"/>
/// (reassembly) and <see cref="EbicsCompression"/> (decompression) — into the two-phase upload state
/// machine, backed by the <see cref="IUploadTransactionStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The transaction-level and segment-level conditions (unknown/duplicate/over-run segments, unknown
/// transaction id, segment count limits) are control flow and are returned directly as business return
/// codes. The order-data decode failures (undecryptable/undecompressable data) are wrapped via
/// <see cref="OrderDataFault"/> and reported as <see cref="EbicsReturnCode.InvalidOrderDataFormat"/>,
/// consistent with the single-phase order handlers.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the order signature (ES) carried in the initialisation is retained on the
/// transaction but <em>not</em> verified in this issue (see <c>docs/server/upload-transaction.md</c>);
/// the X002 request authentication signature likewise stays unverified (the pipeline verify stage is a
/// no-op). The canonical Initialisation/Transfer split and the exact segment semantics are to be
/// verified against the official EBICS annexes.
/// </para>
/// </remarks>
public sealed class UploadTransactionEngine : IUploadTransactionEngine, ITransactionEvictor
{
    /// <summary>The H003/H004 generic upload order type (file upload).</summary>
    public const string FulOrderType = "FUL";

    /// <summary>The H005 generic upload order type (business transaction upload).</summary>
    public const string BtuOrderType = "BTU";

    private readonly IUploadTransactionStore _store;
    private readonly IMasterDataManager _masterData;
    private readonly IServerBankKeyStore _bankKeyStore;
    private readonly IEventLog _eventLog;
    private readonly IUploadOrderProcessor _orderProcessor;
    private readonly TimeProvider _timeProvider;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the engine.</summary>
    /// <param name="store">The upload transaction store.</param>
    /// <param name="masterData">The master-data manager used to resolve the subscriber.</param>
    /// <param name="bankKeyStore">The store providing the bank's key pair (its private encryption key decrypts the transaction key).</param>
    /// <param name="eventLog">The append-only event log (issue #69) the upload lifecycle events are written to.</param>
    /// <param name="orderProcessor">The order-type-specific processor invoked once the order data is decoded (issue #39).</param>
    /// <param name="timeProvider">The clock used to stamp transaction creation.</param>
    /// <param name="options">The server options (segment/transaction limits).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public UploadTransactionEngine(
        IUploadTransactionStore store,
        IMasterDataManager masterData,
        IServerBankKeyStore bankKeyStore,
        IEventLog eventLog,
        IUploadOrderProcessor orderProcessor,
        TimeProvider timeProvider,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(bankKeyStore);
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(orderProcessor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _store = store;
        _masterData = masterData;
        _bankKeyStore = bankKeyStore;
        _eventLog = eventLog;
        _orderProcessor = orderProcessor;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    /// <summary>
    /// Whether <paramref name="orderType"/> routes to the upload engine: the generic upload order types
    /// (<see cref="FulOrderType"/> for H003/H004, <see cref="BtuOrderType"/> for H005) or a classical
    /// upload order-type code submitted directly (e.g. <c>"CCT"</c>/<c>"CDD"</c>, via
    /// <see cref="BtfOrderTypeCatalog.IsUploadOrderType"/>).
    /// </summary>
    /// <param name="orderType">The extracted order type, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for an upload order type; otherwise <see langword="false"/>.</returns>
    public static bool IsUploadOrderType(string? orderType)
        => orderType is FulOrderType or BtuOrderType || BtfOrderTypeCatalog.IsUploadOrderType(orderType);

    /// <inheritdoc />
    public async Task<UploadTransactionResult> BeginUploadAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fields = ExtractInit(context.Envelope);

        if (!HostId.TryCreate(fields.HostId, out var hostId)
            || !PartnerId.TryCreate(fields.PartnerId, out var partnerId)
            || !UserId.TryCreate(fields.UserId, out var userId))
        {
            return Init(EbicsReturnCode.InvalidUserOrUserState);
        }

        // The upload requires a fully onboarded subscriber (INI + HIA done).
        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.Ready)
        {
            return Init(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Authorisation per BTF/order type (issue #38): the subscriber must hold a permission for the
        // requested order type. The effective code is resolved across the three upload conventions
        // (issue #39): H005 BTU (BTUOrderParams/Service), H003/H004 generic FUL + FULOrderParams/FileFormat,
        // or a classical order type submitted directly. Authorising against the resolved code fixes the
        // FUL case (otherwise the check would run against "FUL", not e.g. "CCT").
        var effectiveOrderType = BtfOrderTypeCatalog.ResolveUploadOrderType(context.OrderType, context.Btf, fields.FileFormat);
        if (effectiveOrderType is null || !subscriber.HasPermissionFor(effectiveOrderType))
        {
            return Init(EbicsReturnCode.AuthorisationOrderTypeFailed);
        }

        // NumSegments is mandatory for uploads and must be within the configured ceiling.
        if (fields.NumSegments is not { } numSegments || numSegments == 0)
        {
            return Init(EbicsReturnCode.InvalidRequestContent);
        }

        if (numSegments > (ulong)_options.MaxUploadSegments)
        {
            return Init(EbicsReturnCode.MaxSegmentsExceeded);
        }

        // Soft ceiling on concurrent transactions (0 = unlimited). Counts completed-but-not-yet-evicted
        // uploads within the retention window; the count-then-create is not atomic (acceptable for the
        // emulator).
        if (_options.MaxConcurrentTransactions > 0 && _store.Count >= _options.MaxConcurrentTransactions)
        {
            return Init(EbicsReturnCode.MaxTransactionsExceeded);
        }

        if (fields.TransactionKey is not { } encryptedTransactionKey)
        {
            return Init(EbicsReturnCode.InvalidOrderDataFormat);
        }

        // Decrypt the transaction key with the bank's private encryption key (the reverse of the client's
        // RSA-OAEP encryption). Undecryptable key material surfaces as InvalidOrderDataFormat.
        var bankKeys = await _bankKeyStore.GetOrCreateAsync(hostId, ct).ConfigureAwait(false);
        byte[] transactionKey;
        try
        {
            transactionKey = OrderDataFault.Wrap(() =>
                EncryptionE002.DecryptTransactionKey(encryptedTransactionKey, bankKeys.Encryption, bankKeys.EncryptionVersion));
        }
        catch (EbicsOrderDataException)
        {
            return Init(EbicsReturnCode.InvalidOrderDataFormat);
        }

        var transactionId = RandomNumberGenerator.GetBytes(16);
        var transaction = new UploadTransaction(
            transactionId,
            context.Version,
            new SubscriberKeyRef(hostId, partnerId, userId),
            context.OrderType!,
            (int)numSegments,
            transactionKey,
            fields.SignatureData,
            _timeProvider.GetUtcNow(),
            effectiveOrderType);

        _store.Create(transaction);

        await AppendEventAsync(
            transaction,
            EbicsEventType.UploadStarted,
            EbicsEventSeverity.Info,
            EbicsEventVisibility.CustomerVisible,
            EbicsReturnCode.Ok,
            $"Upload started ({transaction.NumSegments} segment(s), order type {transaction.OrderType}).",
            ct).ConfigureAwait(false);

        return new UploadTransactionResult(EbicsReturnCode.Ok, EbicsTransactionPhase.Initialisation, transactionId);
    }

    /// <inheritdoc />
    public async Task<UploadTransactionResult> ContinueUploadAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fields = ExtractTransfer(context.Envelope);

        // Without a transaction id there is nothing to continue; there is no id to echo either.
        if (fields.TransactionId is not { } transactionId)
        {
            return Transfer(EbicsReturnCode.TxUnknownTxid, null, fields.SegmentNumber, fields.LastSegment);
        }

        if (!_store.TryGet(Convert.ToHexString(transactionId), out var transaction) || transaction is null)
        {
            return Transfer(EbicsReturnCode.TxUnknownTxid, transactionId, fields.SegmentNumber, fields.LastSegment);
        }

        // Lazy expiry: an idle-timed-out transaction is evicted and answered as an unknown id (091101),
        // just as if the background sweeper had already removed it. A live one has its idle window slid.
        var now = _timeProvider.GetUtcNow();
        if (transaction.IsExpired(now, _options.TransactionTimeout))
        {
            _store.Remove(transaction.TransactionIdHex);
            return Transfer(EbicsReturnCode.TxUnknownTxid, transactionId, fields.SegmentNumber, fields.LastSegment);
        }

        transaction.Touch(now);

        if (fields.SegmentNumber is not { } segmentNumber || fields.OrderData is not { } orderData)
        {
            return Transfer(EbicsReturnCode.InvalidRequestContent, transactionId, fields.SegmentNumber, fields.LastSegment);
        }

        // Segments are 1-based; a number beyond the announced count is a protocol error.
        if (segmentNumber == 0)
        {
            return Transfer(EbicsReturnCode.InvalidRequestContent, transactionId, segmentNumber, fields.LastSegment);
        }

        if (segmentNumber > (ulong)transaction.NumSegments)
        {
            return Transfer(EbicsReturnCode.TxSegmentNumberExceeded, transactionId, segmentNumber, fields.LastSegment);
        }

        var append = transaction.AppendSegment((int)segmentNumber, orderData, fields.LastSegment);

        var returnCode = append.Status switch
        {
            SegmentAppendStatus.Duplicate => EbicsReturnCode.TxMessageReplay,
            SegmentAppendStatus.Underrun => EbicsReturnCode.TxSegmentNumberUnderrun,
            SegmentAppendStatus.Buffered => EbicsReturnCode.Ok,
            SegmentAppendStatus.Ready => await FinalizeOrderAsync(transaction, append.OrderedSegments!, ct).ConfigureAwait(false),
            _ => EbicsReturnCode.InternalError,
        };

        // The last segment completed the order (reassembled/decrypted/decompressed): a business-level event.
        if (append.Status == SegmentAppendStatus.Ready && returnCode.Code == EbicsReturnCode.OkCode)
        {
            await AppendEventAsync(
                transaction,
                EbicsEventType.UploadCompleted,
                EbicsEventSeverity.Info,
                EbicsEventVisibility.CustomerVisible,
                EbicsReturnCode.Ok,
                $"Upload completed ({transaction.NumSegments} segment(s), order type {transaction.OrderType}).",
                ct).ConfigureAwait(false);
        }

        return Transfer(returnCode, transactionId, segmentNumber, fields.LastSegment);
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
            if (transaction.IsExpired(now, timeout) && _store.Remove(transaction.TransactionIdHex))
            {
                await AppendEventAsync(
                    transaction,
                    EbicsEventType.TransactionEvicted,
                    EbicsEventSeverity.Warning,
                    EbicsEventVisibility.Internal,
                    returnCode: null,
                    $"Upload transaction evicted after idle timeout (order type {transaction.OrderType}).",
                    ct).ConfigureAwait(false);
                evicted++;
            }
        }

        return evicted;
    }

    // Writes a transaction lifecycle event (issue #69) carrying the transaction's full subscriber triple,
    // order type and hex id. Complements the pipeline's per-request event with business-level semantics.
    private Task AppendEventAsync(
        UploadTransaction transaction,
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

    // Reassembles, decrypts and decompresses the completed order data, runs the order-type-specific
    // processing (issue #39) and records the plaintext on the transaction. Decode failures map to
    // InvalidOrderDataFormat (090004); a payment order whose payload fails validation is rejected with the
    // processor's return code (090004) and is NOT retained. The order signature (ES) is not verified in
    // this issue (Spec-Vorbehalt).
    private async Task<EbicsReturnCode> FinalizeOrderAsync(
        UploadTransaction transaction, IReadOnlyList<byte[]> orderedSegments, CancellationToken ct)
    {
        byte[] orderData;
        try
        {
            orderData = OrderDataFault.Wrap(() =>
            {
                var ciphertext = EbicsSegmentation.Reassemble(orderedSegments);
                var compressed = EncryptionE002.DecryptOrderData(ciphertext, transaction.TransactionKey);
                return EbicsCompression.Decompress(compressed);
            });
        }
        catch (EbicsOrderDataException)
        {
            return EbicsReturnCode.InvalidOrderDataFormat;
        }

        // Order-type-specific processing (issue #39): a payment upload (CCT/CDD/CDB/CIP) is validated and
        // its pain.002 status report filed for later delivery here. Order types the processor does not
        // handle keep the previous behaviour: retain the plaintext on the transaction, no processing.
        if (transaction.EffectiveOrderType is { } effectiveOrderType && _orderProcessor.CanProcess(effectiveOrderType))
        {
            var processing = await _orderProcessor.ProcessAsync(
                new UploadOrderContext(
                    transaction.Subscriber,
                    transaction.Version,
                    effectiveOrderType,
                    orderData,
                    transaction.TransactionIdHex),
                ct).ConfigureAwait(false);

            // A rejected payload is not completed/retained; the transfer step reports the rejection code.
            if (processing.ReturnCode.Code != EbicsReturnCode.OkCode)
            {
                return processing.ReturnCode;
            }
        }

        transaction.Complete(orderData);
        return EbicsReturnCode.Ok;
    }

    private static UploadTransactionResult Init(EbicsReturnCode returnCode)
        => new(returnCode, EbicsTransactionPhase.Initialisation);

    private static UploadTransactionResult Transfer(EbicsReturnCode returnCode, byte[]? transactionId, ulong? segmentNumber, bool lastSegment)
        => new(returnCode, EbicsTransactionPhase.Transfer, transactionId, segmentNumber, lastSegment);

    // --- Version-neutral field extraction (mirrors EbicsRequestPipeline.TryExtractOrderType) --------

    private readonly record struct InitFields(
        string? HostId, string? PartnerId, string? UserId, ulong? NumSegments, byte[]? TransactionKey, byte[]? SignatureData, string? FileFormat);

    private readonly record struct TransferFields(
        byte[]? TransactionId, ulong? SegmentNumber, bool LastSegment, byte[]? OrderData);

    private static InitFields ExtractInit(IEbicsRequestEnvelope envelope) => envelope switch
    {
        // H003/H004 carry the pain format in FULOrderParams/FileFormat (generic file upload); H005 carries
        // the business identity in the BTF (context.Btf), so it has no FileFormat here.
        H003.EbicsRequest r => new InitFields(
            r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId,
            r.Header?.Static?.NumSegments,
            r.Body?.DataTransfer?.DataEncryptionInfo?.TransactionKey,
            r.Body?.DataTransfer?.SignatureData?.Value,
            (r.Header?.Static?.OrderDetails?.OrderParams as H003.FulOrderParamsType)?.FileFormat?.Value),
        H004.EbicsRequest r => new InitFields(
            r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId,
            r.Header?.Static?.NumSegments,
            r.Body?.DataTransfer?.DataEncryptionInfo?.TransactionKey,
            r.Body?.DataTransfer?.SignatureData?.Value,
            (r.Header?.Static?.OrderDetails?.OrderParams as H004.FulOrderParamsType)?.FileFormat?.Value),
        H005.EbicsRequest r => new InitFields(
            r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId,
            r.Header?.Static?.NumSegments,
            r.Body?.DataTransfer?.DataEncryptionInfo?.TransactionKey,
            r.Body?.DataTransfer?.SignatureData?.Value,
            FileFormat: null),
        _ => default,
    };

    private static TransferFields ExtractTransfer(IEbicsRequestEnvelope envelope) => envelope switch
    {
        H003.EbicsRequest r => new TransferFields(
            r.Header?.Static?.TransactionId,
            r.Header?.Mutable?.SegmentNumber?.Value,
            r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false,
            r.Body?.DataTransfer?.OrderData?.Value),
        H004.EbicsRequest r => new TransferFields(
            r.Header?.Static?.TransactionId,
            r.Header?.Mutable?.SegmentNumber?.Value,
            r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false,
            r.Body?.DataTransfer?.OrderData?.Value),
        H005.EbicsRequest r => new TransferFields(
            r.Header?.Static?.TransactionId,
            r.Header?.Mutable?.SegmentNumber?.Value,
            r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false,
            r.Body?.DataTransfer?.OrderData?.Value),
        _ => default,
    };
}
