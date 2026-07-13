using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="EbicsRequestPipeline"/> — the HTTP-free orchestrator of the server host
/// skeleton (issue #25): parse → dispatch → verify → handle → respond, exercised directly on raw
/// XML. The skeleton registers no order handlers, so recognized requests are answered with
/// <c>EBICS_UNSUPPORTED_ORDER_TYPE</c>.
/// </summary>
public class EbicsRequestPipelineTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static IEbicsRequestPipeline BuildPipeline(Action<IServiceCollection>? customize = null)
    {
        var services = new ServiceCollection();
        customize?.Invoke(services);   // register overrides/handlers before the TryAdd* defaults
        services.AddEbicoServer();
        return services.BuildServiceProvider().GetRequiredService<IEbicsRequestPipeline>();
    }

    private sealed class FailingVerifier(EbicsReturnCode failure) : IEbicsRequestVerifier
    {
        public Task<EbicsVerificationResult> VerifyAsync(EbicsRequestContext context, CancellationToken ct = default)
            => Task.FromResult(EbicsVerificationResult.Fail(failure));
    }

    private sealed class StubHandler(EbicsVersion version, string orderType, EbicsReturnCode returnCode) : IEbicsOrderHandler
    {
        public EbicsVersion Version { get; } = version;

        public string OrderType { get; } = orderType;

        public Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
            => Task.FromResult(new EbicsOrderResult(returnCode));
    }

    private static (string? HeaderCode, string? BodyCode) ReadCodes(EbicsPipelineResult result)
    {
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
        return ServerTestHelpers.ReadReturnCodes(envelope);
    }

    [Fact]
    public async Task ProcessAsync_MalformedXml_ReturnsInvalidXml_InFallbackVersion()
    {
        var result = await BuildPipeline().ProcessAsync("<not-xml", _ct);

        result.Version.Should().Be(EbicsVersion.H005);
        ReadCodes(result).BodyCode.Should().Be("091010");
    }

    [Fact]
    public async Task ProcessAsync_EmptyXml_ReturnsInvalidXml()
    {
        var result = await BuildPipeline().ProcessAsync(string.Empty, _ct);

        ReadCodes(result).BodyCode.Should().Be("091010");
    }

    [Fact]
    public async Task ProcessAsync_UnsupportedNamespace_ReturnsInvalidRequest_InFallbackVersion()
    {
        var result = await BuildPipeline().ProcessAsync("<ebicsRequest xmlns=\"urn:example:unknown\"/>", _ct);

        result.Version.Should().Be(EbicsVersion.H005);
        ReadCodes(result).HeaderCode.Should().Be("061002");
    }

    [Fact]
    public async Task ProcessAsync_WellFormedRequestUnknownOrderType_ReturnsUnsupportedOrderType()
    {
        var xml = ServerTestHelpers.BuildH004Request("AAA");

        var result = await BuildPipeline().ProcessAsync(xml, _ct);

        result.Version.Should().Be(EbicsVersion.H004);
        ReadCodes(result).BodyCode.Should().Be("091006");
    }

    [Fact]
    public async Task ProcessAsync_ResponseEnvelopeAsInput_ReturnsInvalidRequest()
    {
        // An ebicsResponse is a server→client envelope, not a valid inbound request.
        var result = await BuildPipeline().ProcessAsync("<ebicsResponse xmlns=\"urn:org:ebics:H004\"/>", _ct);

        result.Version.Should().Be(EbicsVersion.H004);
        ReadCodes(result).HeaderCode.Should().Be("061002");
    }

    [Fact]
    public async Task ProcessAsync_WellFormedRootMalformedBody_ReturnsInvalidXml()
    {
        // The root start tag is valid (version detectable), but the body is not well-formed, so the
        // failure surfaces only during deserialization — it must still map to the client XML error.
        const string xml = "<ebicsRequest xmlns=\"urn:org:ebics:H004\"><header></ebicsRequest>";

        var result = await BuildPipeline().ProcessAsync(xml, _ct);

        result.Version.Should().Be(EbicsVersion.H004);
        ReadCodes(result).BodyCode.Should().Be("091010");
    }

    [Fact]
    public async Task ProcessAsync_VerifierFails_ReturnsVerifierFailureCode()
    {
        var pipeline = BuildPipeline(s =>
            s.AddSingleton<IEbicsRequestVerifier>(new FailingVerifier(EbicsReturnCode.AuthenticationFailed)));

        var result = await pipeline.ProcessAsync(ServerTestHelpers.BuildH004Request("AAA"), _ct);

        result.Version.Should().Be(EbicsVersion.H004);
        ReadCodes(result).HeaderCode.Should().Be("061001");
    }

    [Fact]
    public async Task ProcessAsync_MatchingHandler_ReturnsHandlerReturnCode()
    {
        // A registered handler for (H004, "AAA") is resolved and its return code flows through;
        // 000000/000000 is only reachable when verify passes and a handler returns EBICS_OK.
        var pipeline = BuildPipeline(s =>
            s.AddSingleton<IEbicsOrderHandler>(new StubHandler(EbicsVersion.H004, "AAA", EbicsReturnCode.Ok)));

        var result = await pipeline.ProcessAsync(ServerTestHelpers.BuildH004Request("AAA"), _ct);

        var (headerCode, bodyCode) = ReadCodes(result);
        headerCode.Should().Be("000000");
        bodyCode.Should().Be("000000");
    }
}
