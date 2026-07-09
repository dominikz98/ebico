using EBICO.Core;
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

        EbicsReturnCode returnCode;
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
                var context = new EbicsRequestContext(
                    requestXml,
                    EbicsVersions.Get(responseVersion),
                    request,
                    TryExtractOrderType(request));

                returnCode = await RunVerifyAndHandleAsync(context, ct).ConfigureAwait(false);
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

        // Stage 5: Respond.
        var response = _responseFactory.BuildErrorResponse(responseVersion, returnCode);
        var body = EbicsXmlSerializer.SerializeToUtf8Bytes(response);
        return new EbicsPipelineResult(body, responseVersion);
    }

    private async Task<EbicsReturnCode> RunVerifyAndHandleAsync(EbicsRequestContext context, CancellationToken ct)
    {
        // Stage 3: Verify (extension point; default no-op).
        var verification = await _verifier.VerifyAsync(context, ct).ConfigureAwait(false);
        if (!verification.IsVerified)
        {
            return verification.Failure ?? EbicsReturnCode.AuthenticationFailed;
        }

        // Stage 4: Handle (extension point; the skeleton registers no handlers).
        var handler = _resolver.Resolve(context.Version, context.OrderType);
        if (handler is null)
        {
            return string.IsNullOrEmpty(context.OrderType)
                ? EbicsReturnCode.InvalidOrderType
                : EbicsReturnCode.UnsupportedOrderType;
        }

        var result = await handler.HandleAsync(context, ct).ConfigureAwait(false);
        return result.ReturnCode;
    }

    // Best-effort order-type extraction from the standard ebicsRequest header. The
    // unsecured/unsigned/no-pub-key-digests requests (INI/HIA/HPB) carry the order type elsewhere;
    // that extraction lands with the M3/M4 key-management issues.
    private static string? TryExtractOrderType(IEbicsRequestEnvelope request) => request switch
    {
        H003.EbicsRequest r => r.Header?.Static?.OrderDetails?.OrderType?.Value,
        H004.EbicsRequest r => r.Header?.Static?.OrderDetails?.OrderType?.Value,
        H005.EbicsRequest r => r.Header?.Static?.OrderDetails?.AdminOrderType?.Value,
        _ => null,
    };
}
