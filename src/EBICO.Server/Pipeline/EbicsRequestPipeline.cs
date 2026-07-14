using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;
using EBICO.Server.Transactions;
using Microsoft.Extensions.Options;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Server.Pipeline;

/// <summary>
/// Default <see cref="IEbicsRequestPipeline"/>. Runs the five stages
/// (Parse → Version-Dispatch → Verify → Handle → Respond) over the raw request body, mapping every
/// protocol/business error onto an EBICS return code carried in a well-formed <c>ebicsResponse</c>.
/// </summary>
/// <remarks>
/// Parsing and version dispatch reuse <see cref="EbicsXmlSerializer.DeserializeEnvelope"/> (XXE
/// hardened). Verify and Handle are extension points with no-op / no-handler defaults in this
/// skeleton, so a recognized request is answered with
/// <see cref="EbicsReturnCode.UnsupportedOrderType"/>.
/// </remarks>
public sealed class EbicsRequestPipeline : IEbicsRequestPipeline
{
    private readonly IEbicsRequestVerifier _verifier;
    private readonly IEbicsOrderHandlerResolver _resolver;
    private readonly IUploadTransactionEngine _uploadEngine;
    private readonly IDownloadTransactionEngine _downloadEngine;
    private readonly IEbicsErrorMapper _errorMapper;
    private readonly EbicsResponseFactory _responseFactory;
    private readonly IEventLog _eventLog;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the pipeline with its collaborators.</summary>
    /// <param name="verifier">The verify-stage extension point.</param>
    /// <param name="resolver">The handle-stage order handler resolver.</param>
    /// <param name="uploadEngine">The upload transaction engine (issue #32) that owns the upload phases.</param>
    /// <param name="downloadEngine">The download transaction engine (issue #33) that owns the download phases.</param>
    /// <param name="errorMapper">The exception-to-return-code mapper.</param>
    /// <param name="responseFactory">The response envelope factory.</param>
    /// <param name="eventLog">The append-only event log (issue #69) the central per-request event is written to.</param>
    /// <param name="options">The server options.</param>
    public EbicsRequestPipeline(
        IEbicsRequestVerifier verifier,
        IEbicsOrderHandlerResolver resolver,
        IUploadTransactionEngine uploadEngine,
        IDownloadTransactionEngine downloadEngine,
        IEbicsErrorMapper errorMapper,
        EbicsResponseFactory responseFactory,
        IEventLog eventLog,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(uploadEngine);
        ArgumentNullException.ThrowIfNull(downloadEngine);
        ArgumentNullException.ThrowIfNull(errorMapper);
        ArgumentNullException.ThrowIfNull(responseFactory);
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(options);

        _verifier = verifier;
        _resolver = resolver;
        _uploadEngine = uploadEngine;
        _downloadEngine = downloadEngine;
        _errorMapper = errorMapper;
        _responseFactory = responseFactory;
        _eventLog = eventLog;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<EbicsPipelineResult> ProcessAsync(string requestXml, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requestXml);

        // Default response version; overwritten once the request's actual version is known.
        var responseVersion = _options.FallbackResponseVersion;

        // Whether to answer with an ebicsKeyManagementResponse (INI/HIA/... unsecured requests) rather
        // than a plain ebicsResponse. Only known once the request has parsed; the error paths (parse
        // failure, non-request envelope) fall back to ebicsResponse.
        var keyManagement = false;

        EbicsReturnCode returnCode;

        // The encrypted payload a successful download order (HPB) contributes to its response; stays
        // null for INI/HIA (pure return-code responses) and every error path.
        EbicsKeyManagementPayload? payload = null;

        // The result of an upload transaction step (issue #32); non-null routes the respond stage to the
        // transaction-shaped ebicsResponse (transaction id + phase). Stays null for every other request.
        UploadTransactionResult? transaction = null;

        // The result of a download transaction step (issue #33); non-null routes the respond stage to the
        // download-shaped ebicsResponse (transaction id + phase + segment). Stays null otherwise.
        DownloadTransactionResult? download = null;

        // The parsed request context, hoisted so the central event write point (issue #69) can read the
        // subscriber/order type/phase after the try/catch. Stays null on the parse/non-request error paths.
        EbicsRequestContext? context = null;
        try
        {
            // Stage 1 + 2: Parse and dispatch on version + root element (single parse; the version
            // comes straight off the parsed envelope, no separate detection pass).
            var envelope = EbicsXmlSerializer.DeserializeEnvelope(requestXml);
            responseVersion = envelope.ProtocolVersion;

            if (envelope is not IEbicsRequestEnvelope request)
            {
                // A response (or otherwise non-request) envelope is not a valid inbound message.
                returnCode = EbicsReturnCode.InvalidRequest;
            }
            else
            {
                keyManagement = IsKeyManagementRequest(request);

                context = new EbicsRequestContext(
                    requestXml,
                    EbicsVersions.Get(responseVersion),
                    request,
                    TryExtractOrderType(request),
                    TryExtractTransactionPhase(request),
                    TryExtractTransactionId(request));

                var result = await RunVerifyAndHandleAsync(context, ct).ConfigureAwait(false);
                returnCode = result.ReturnCode;
                payload = result.Payload;
                transaction = result.Upload;
                download = result.Download;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recover the version for the error response when the root namespace was recognizable
            // (e.g. a recognized envelope with a malformed body); otherwise keep the fallback.
            if (EbicsVersionDetector.TryDetect(requestXml, out var detected) && detected is not null)
            {
                responseVersion = detected.Version;
            }

            returnCode = _errorMapper.Map(ex);
        }

        // Central event write point (issue #69): one event per processed request, recording the
        // protocol-level "request answered" outcome. The transaction engines add their own richer
        // lifecycle events; this is the backbone both projections (HAC/Suite) read.
        await AppendRequestEventAsync(context, returnCode, transaction, download, ct).ConfigureAwait(false);

        // Stage 5: Respond. An upload transaction step (issue #32) is answered with a transaction-shaped
        // ebicsResponse (transaction id + phase); a download transaction step (issue #33) with a
        // download-shaped ebicsResponse (transaction id + phase + segment). Key-management orders
        // (INI/HIA/HPB) are answered with an ebicsKeyManagementResponse; everything else with a plain
        // ebicsResponse. A successful HPB also carries an encrypted DataTransfer (the bank's public keys)
        // via the payload overload.
        IEbicsResponseEnvelope response;
        if (transaction is { } tx)
        {
            response = _responseFactory.BuildTransactionResponse(
                responseVersion, tx.Phase, tx.TransactionId, tx.ReturnCode, tx.SegmentNumber, tx.LastSegment);
        }
        else if (download is { } dtx)
        {
            response = _responseFactory.BuildDownloadResponse(responseVersion, dtx);
        }
        else
        {
            response = (keyManagement, payload) switch
            {
                (true, not null) => _responseFactory.BuildKeyManagementResponse(responseVersion, payload),
                (true, null) => _responseFactory.BuildKeyManagementResponse(responseVersion, returnCode),
                _ => _responseFactory.BuildErrorResponse(responseVersion, returnCode),
            };
        }

        var body = EbicsXmlSerializer.SerializeToUtf8Bytes(response);
        return new EbicsPipelineResult(body, responseVersion);
    }

    private async Task<(EbicsReturnCode ReturnCode, EbicsKeyManagementPayload? Payload, UploadTransactionResult? Upload, DownloadTransactionResult? Download)>
        RunVerifyAndHandleAsync(EbicsRequestContext context, CancellationToken ct)
    {
        // Stage 3: Verify (extension point; default no-op). Runs for every request, transactions included.
        var verification = await _verifier.VerifyAsync(context, ct).ConfigureAwait(false);
        if (!verification.IsVerified)
        {
            return (verification.Failure ?? EbicsReturnCode.AuthenticationFailed, null, null, null);
        }

        // Stage 4a: Transaction engines (issues #32/#33). Only the signed ebicsRequest carries a
        // transaction. Phases are routed here, before the resolver:
        //   - Receipt is download-only (uploads have no receipt phase).
        //   - A transfer-phase request carries only a transaction id; the download engine claims it when
        //     the id is one of its transactions (OwnsTransaction), otherwise it is an upload transfer.
        //     Robustness: route transfers on the presence of a transaction id too, since an omitted
        //     <TransactionPhase> deserializes silently to Initialisation.
        //   - Initialisation is routed by order type: FUL/BTU -> upload, FDL/BTD -> download.
        if (context.Envelope is H003.EbicsRequest or H004.EbicsRequest or H005.EbicsRequest)
        {
            if (context.TransactionPhase == EbicsTransactionPhase.Receipt)
            {
                var receipt = await _downloadEngine.AcknowledgeReceiptAsync(context, ct).ConfigureAwait(false);
                return (receipt.ReturnCode, null, null, receipt);
            }

            if (context.TransactionId is not null || context.TransactionPhase == EbicsTransactionPhase.Transfer)
            {
                if (_downloadEngine.OwnsTransaction(context.TransactionId))
                {
                    var downloadTransfer = await _downloadEngine.ContinueDownloadAsync(context, ct).ConfigureAwait(false);
                    return (downloadTransfer.ReturnCode, null, null, downloadTransfer);
                }

                var uploadTransfer = await _uploadEngine.ContinueUploadAsync(context, ct).ConfigureAwait(false);
                return (uploadTransfer.ReturnCode, null, uploadTransfer, null);
            }

            if (context.TransactionPhase == EbicsTransactionPhase.Initialisation)
            {
                if (UploadTransactionEngine.IsUploadOrderType(context.OrderType))
                {
                    var uploadInit = await _uploadEngine.BeginUploadAsync(context, ct).ConfigureAwait(false);
                    return (uploadInit.ReturnCode, null, uploadInit, null);
                }

                if (DownloadTransactionEngine.IsDownloadOrderType(context.OrderType))
                {
                    var downloadInit = await _downloadEngine.BeginDownloadAsync(context, ct).ConfigureAwait(false);
                    return (downloadInit.ReturnCode, null, null, downloadInit);
                }
            }
        }

        // Stage 4b: Handle single-phase order handlers (INI/HIA/HPB/HCA/HCS/SPR/HSA).
        var handler = _resolver.Resolve(context.Version, context.OrderType);
        if (handler is null)
        {
            return (string.IsNullOrEmpty(context.OrderType)
                ? EbicsReturnCode.InvalidOrderType
                : EbicsReturnCode.UnsupportedOrderType, null, null, null);
        }

        var handled = await handler.HandleAsync(context, ct).ConfigureAwait(false);
        return (handled.ReturnCode, handled.Payload, null, null);
    }

    // Writes the central per-request event (issue #69). Subscriber ids come from the request static header
    // (present for initialisations and key-management; a transfer/receipt header carries only the host).
    private async Task AppendRequestEventAsync(
        EbicsRequestContext? context,
        EbicsReturnCode returnCode,
        UploadTransactionResult? upload,
        DownloadTransactionResult? download,
        CancellationToken ct)
    {
        HostId? host = null;
        PartnerId? partner = null;
        UserId? user = null;
        if (context is not null)
        {
            (host, partner, user) = TryExtractSubscriber(context.Envelope);
        }

        var transactionId = context?.TransactionId ?? upload?.TransactionId ?? download?.TransactionId;
        var phase = context?.TransactionPhase;
        var isSuccess = returnCode.Code == EbicsReturnCode.OkCode;
        var isInternalError = returnCode == EbicsReturnCode.InternalError;

        await _eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = EbicsEventType.RequestReceived,
                Severity = isSuccess
                    ? EbicsEventSeverity.Info
                    : isInternalError ? EbicsEventSeverity.Error : EbicsEventSeverity.Warning,
                // Segment-level transfer/receipt steps are operator noise; only initialisations and
                // single-phase orders are customer-visible. Internal errors are never surfaced to a customer.
                Visibility = isInternalError || phase is EbicsTransactionPhase.Transfer or EbicsTransactionPhase.Receipt
                    ? EbicsEventVisibility.Internal
                    : EbicsEventVisibility.CustomerVisible,
                HostId = host,
                PartnerId = partner,
                UserId = user,
                OrderType = context?.OrderType,
                TransactionId = transactionId is null ? null : Convert.ToHexString(transactionId),
                ReturnCode = returnCode,
                Message = $"{context?.OrderType ?? "request"} → {returnCode.SymbolicName}",
            },
            ct).ConfigureAwait(false);
    }

    // Subscriber (Kunde/Teilnehmer) ids from the request static header. A signed ebicsRequest in the
    // transfer/receipt phase carries only HostID (PartnerID/UserID are bound to the transaction), so those
    // come back null; the transaction engines' lifecycle events carry the full triple in that case.
    private static (HostId? Host, PartnerId? Partner, UserId? User) TryExtractSubscriber(IEbicsRequestEnvelope request)
    {
        var (host, partner, user) = request switch
        {
            H003.EbicsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H004.EbicsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H005.EbicsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H003.EbicsUnsecuredRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H004.EbicsUnsecuredRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H005.EbicsUnsecuredRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H003.EbicsNoPubKeyDigestsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H004.EbicsNoPubKeyDigestsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            H005.EbicsNoPubKeyDigestsRequest r => (r.Header?.Static?.HostId, r.Header?.Static?.PartnerId, r.Header?.Static?.UserId),
            _ => (default(string), default(string), default(string)),
        };

        return (
            HostId.TryCreate(host, out var h) ? h : null,
            PartnerId.TryCreate(partner, out var p) ? p : null,
            UserId.TryCreate(user, out var u) ? u : null);
    }

    // Order-type extraction. The standard ebicsRequest carries it in OrderDetails (H003/H004:
    // OrderType, H005: AdminOrderType); the unsecured key-management requests (INI/HIA) and the
    // no-pub-key-digests request (HPB) carry it in the same OrderDetails element of their own header.
    // The remaining request shape (unsigned) is dispatched by later issues.
    private static string? TryExtractOrderType(IEbicsRequestEnvelope request) => request switch
    {
        H003.EbicsRequest r => r.Header?.Static?.OrderDetails?.OrderType?.Value,
        H004.EbicsRequest r => r.Header?.Static?.OrderDetails?.OrderType?.Value,
        H005.EbicsRequest r => r.Header?.Static?.OrderDetails?.AdminOrderType?.Value,
        H003.EbicsUnsecuredRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H004.EbicsUnsecuredRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H005.EbicsUnsecuredRequest r => r.Header?.Static?.OrderDetails?.AdminOrderType,
        H003.EbicsNoPubKeyDigestsRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H004.EbicsNoPubKeyDigestsRequest r => r.Header?.Static?.OrderDetails?.OrderType,
        H005.EbicsNoPubKeyDigestsRequest r => r.Header?.Static?.OrderDetails?.AdminOrderType,
        _ => null,
    };

    // Transaction phase of the signed ebicsRequest (used to route the transaction engine). The
    // unsecured / no-pub-key-digests requests carry no phase.
    private static EbicsTransactionPhase? TryExtractTransactionPhase(IEbicsRequestEnvelope request) => request switch
    {
        H003.EbicsRequest r => MapPhase(r.Header?.Mutable?.TransactionPhase),
        H004.EbicsRequest r => MapPhase(r.Header?.Mutable?.TransactionPhase),
        H005.EbicsRequest r => MapPhase(r.Header?.Mutable?.TransactionPhase),
        _ => null,
    };

    // Transaction id from the signed ebicsRequest static header (present in the transfer phase).
    private static byte[]? TryExtractTransactionId(IEbicsRequestEnvelope request) => request switch
    {
        H003.EbicsRequest r => r.Header?.Static?.TransactionId,
        H004.EbicsRequest r => r.Header?.Static?.TransactionId,
        H005.EbicsRequest r => r.Header?.Static?.TransactionId,
        _ => null,
    };

    private static EbicsTransactionPhase? MapPhase(H003.TransactionPhaseType? phase) => phase switch
    {
        H003.TransactionPhaseType.Transfer => EbicsTransactionPhase.Transfer,
        H003.TransactionPhaseType.Receipt => EbicsTransactionPhase.Receipt,
        H003.TransactionPhaseType.Initialisation => EbicsTransactionPhase.Initialisation,
        _ => null,
    };

    private static EbicsTransactionPhase? MapPhase(H004.TransactionPhaseType? phase) => phase switch
    {
        H004.TransactionPhaseType.Transfer => EbicsTransactionPhase.Transfer,
        H004.TransactionPhaseType.Receipt => EbicsTransactionPhase.Receipt,
        H004.TransactionPhaseType.Initialisation => EbicsTransactionPhase.Initialisation,
        _ => null,
    };

    private static EbicsTransactionPhase? MapPhase(H005.TransactionPhaseType? phase) => phase switch
    {
        H005.TransactionPhaseType.Transfer => EbicsTransactionPhase.Transfer,
        H005.TransactionPhaseType.Receipt => EbicsTransactionPhase.Receipt,
        H005.TransactionPhaseType.Initialisation => EbicsTransactionPhase.Initialisation,
        _ => null,
    };

    // Whether the request is a key-management request answered with an ebicsKeyManagementResponse
    // rather than an ebicsResponse: the unsecured requests (INI/HIA) and the no-pub-key-digests
    // request (HPB).
    private static bool IsKeyManagementRequest(IEbicsRequestEnvelope request) => request
        is H003.EbicsUnsecuredRequest or H004.EbicsUnsecuredRequest or H005.EbicsUnsecuredRequest
        or H003.EbicsNoPubKeyDigestsRequest or H004.EbicsNoPubKeyDigestsRequest or H005.EbicsNoPubKeyDigestsRequest;
}
