using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.ReturnCodes;
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
    private readonly IEbicsErrorMapper _errorMapper;
    private readonly EbicsResponseFactory _responseFactory;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the pipeline with its collaborators.</summary>
    /// <param name="verifier">The verify-stage extension point.</param>
    /// <param name="resolver">The handle-stage order handler resolver.</param>
    /// <param name="errorMapper">The exception-to-return-code mapper.</param>
    /// <param name="responseFactory">The response envelope factory.</param>
    /// <param name="options">The server options.</param>
    public EbicsRequestPipeline(
        IEbicsRequestVerifier verifier,
        IEbicsOrderHandlerResolver resolver,
        IEbicsErrorMapper errorMapper,
        EbicsResponseFactory responseFactory,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(errorMapper);
        ArgumentNullException.ThrowIfNull(responseFactory);
        ArgumentNullException.ThrowIfNull(options);

        _verifier = verifier;
        _resolver = resolver;
        _errorMapper = errorMapper;
        _responseFactory = responseFactory;
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

                var context = new EbicsRequestContext(
                    requestXml,
                    EbicsVersions.Get(responseVersion),
                    request,
                    TryExtractOrderType(request));

                var result = await RunVerifyAndHandleAsync(context, ct).ConfigureAwait(false);
                returnCode = result.ReturnCode;
                payload = result.Payload;
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

        // Stage 5: Respond. Key-management orders (INI/HIA/HPB) are answered with an
        // ebicsKeyManagementResponse, everything else with an ebicsResponse. A successful HPB also
        // carries an encrypted DataTransfer (the bank's public keys) via the payload overload.
        var response = (keyManagement, payload) switch
        {
            (true, not null) => _responseFactory.BuildKeyManagementResponse(responseVersion, payload),
            (true, null) => _responseFactory.BuildKeyManagementResponse(responseVersion, returnCode),
            _ => _responseFactory.BuildErrorResponse(responseVersion, returnCode),
        };
        var body = EbicsXmlSerializer.SerializeToUtf8Bytes(response);
        return new EbicsPipelineResult(body, responseVersion);
    }

    private async Task<EbicsOrderResult> RunVerifyAndHandleAsync(EbicsRequestContext context, CancellationToken ct)
    {
        // Stage 3: Verify (extension point; default no-op).
        var verification = await _verifier.VerifyAsync(context, ct).ConfigureAwait(false);
        if (!verification.IsVerified)
        {
            return new EbicsOrderResult(verification.Failure ?? EbicsReturnCode.AuthenticationFailed);
        }

        // Stage 4: Handle (extension point; the skeleton registers no handlers).
        var handler = _resolver.Resolve(context.Version, context.OrderType);
        if (handler is null)
        {
            return new EbicsOrderResult(string.IsNullOrEmpty(context.OrderType)
                ? EbicsReturnCode.InvalidOrderType
                : EbicsReturnCode.UnsupportedOrderType);
        }

        return await handler.HandleAsync(context, ct).ConfigureAwait(false);
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

    // Whether the request is a key-management request answered with an ebicsKeyManagementResponse
    // rather than an ebicsResponse: the unsecured requests (INI/HIA) and the no-pub-key-digests
    // request (HPB).
    private static bool IsKeyManagementRequest(IEbicsRequestEnvelope request) => request
        is H003.EbicsUnsecuredRequest or H004.EbicsUnsecuredRequest or H005.EbicsUnsecuredRequest
        or H003.EbicsNoPubKeyDigestsRequest or H004.EbicsNoPubKeyDigestsRequest or H005.EbicsNoPubKeyDigestsRequest;
}
