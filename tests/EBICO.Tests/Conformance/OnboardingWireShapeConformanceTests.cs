extern alias EbicoServer;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Tests.E2E;
using EBICO.Tests.Infrastructure;
using EBICO.Tests.Server;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EBICO.Tests.Conformance;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// Conformance (issue #59), <b>parser / wire-shape tolerance</b> layer. EBICO's own connector emits one
/// fixed request shape (protocol namespace as the unprefixed default, no indentation, no comments); a real
/// third-party client is free to encode the same request differently. This suite reshapes the connector's
/// <em>unsecured</em> onboarding requests on the wire — reindent, comment injection, namespace-prefix move
/// (<see cref="XmlShape"/>) — and asserts the server still drives the subscriber <c>New → Initialized →
/// Ready</c>. It proves the server keys on the namespace URI and the parsed structure, not on EBICO's own
/// serializer conventions.
/// </summary>
/// <remarks>
/// This is deliberately scoped to the unsecured onboarding requests (INI/HIA) and HPB (which the X002
/// verifier skips): for those there is no signature to invalidate, so a semantics-preserving reshape is a
/// fair test of the parser. Reshaping a <em>signed</em> request would prove nothing here — EBICO signs and
/// verifies with the same crypto, so any well-formed variation passes by construction; the one meaningful
/// signed variation (the canonicalization algorithm) lives in
/// <see cref="SignedRequestCanonicalizationConformanceTests"/>. The honest limits of this layer are laid
/// out in <c>docs/development/conformance-real-clients.md</c>.
/// </remarks>
public class OnboardingWireShapeConformanceTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public OnboardingWireShapeConformanceTests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>A foreign wire-shape variation a conforming third-party client may emit.</summary>
    public enum WireShape
    {
        /// <summary>Pretty-print indentation (EBICO emits none).</summary>
        Reindented,

        /// <summary>An XML comment inside the request.</summary>
        Commented,

        /// <summary>The protocol namespace on a prefix rather than the default.</summary>
        Prefixed,
    }

    /// <summary>Every wire shape crossed with every supported EBICS version.</summary>
    public static TheoryData<EbicsVersion, WireShape> Cases
    {
        get
        {
            var data = new TheoryData<EbicsVersion, WireShape>();
            foreach (var version in (EbicsVersion[])[EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005])
            {
                foreach (var shape in Enum.GetValues<WireShape>())
                {
                    data.Add(version, shape);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Onboarding_WithForeignWireShape_StillDrivesSubscriberToReady(EbicsVersion version, WireShape shape)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, $"WS{shape}", installTamperingHandler: true, ct: _ct);

        // Arm the reshape for every outgoing onboarding request. INI/HIA are unsecured and HPB is skipped
        // by the X002 verifier, so a semantics-preserving reshape must leave server behaviour unchanged.
        harness.Tampering!.Tamper = Mutator(shape);

        var results = await harness.OnboardAsync(_ct);

        results.Ini.IsSuccess.Should().BeTrue($"INI failed: {results.Ini.ReturnCode} {results.Ini.ReturnText}");
        results.Hia.IsSuccess.Should().BeTrue($"HIA failed: {results.Hia.ReturnCode} {results.Hia.ReturnText}");
        results.Hpb.IsSuccess.Should().BeTrue($"HPB failed: {results.Hpb.ReturnCode} {results.Hpb.ReturnText}");

        // FingerprintsVerified true proves the connector decrypted the E002 bank-key payload the server
        // returned — the response is unaffected by the request's wire shape, which is the whole point.
        results.Hpb.Value!.FingerprintsVerified.Should().BeTrue();
        (await harness.GetSubscriberAsync(_ct))!.State.Should().Be(SubscriberState.Ready);
    }

    [Fact]
    public void Reindent_PreservesTheCanonicalForm()
    {
        var xml = SampleIniRequest();
        CanonicalXmlComparer.AreEqual(xml, XmlShape.Reindent(xml)).Should().BeTrue();
    }

    [Fact]
    public void InjectComments_PreservesTheCanonicalForm()
    {
        var xml = SampleIniRequest();
        CanonicalXmlComparer.AreEqual(xml, XmlShape.InjectComments(xml)).Should().BeTrue();
    }

    [Fact]
    public void WithRootPrefix_ChangesTheWire_ButNotTheParsedEnvelope()
    {
        var xml = SampleIniRequest();
        var prefixed = XmlShape.WithRootPrefix(xml);

        prefixed.Should().Contain("<eb:ebicsUnsecuredRequest");
        // A prefix move changes the canonical octets — that is exactly why it is a meaningful test.
        CanonicalXmlComparer.AreEqual(xml, prefixed).Should().BeFalse();
        // Yet the request still parses to the same envelope (namespace URI unchanged).
        EbicsXmlSerializer.DeserializeEnvelope(prefixed).ProtocolVersion.Should().Be(EbicsVersion.H004);
    }

    private static Func<string, string> Mutator(WireShape shape) => shape switch
    {
        WireShape.Reindented => XmlShape.Reindent,
        WireShape.Commented => XmlShape.InjectComments,
        WireShape.Prefixed => static s => XmlShape.WithRootPrefix(s),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown wire shape."),
    };

    // A representative unsecured request built from the committed bindings; the shared key pool avoids a
    // per-test RSA generation.
    private static string SampleIniRequest()
        => ServerTestHelpers.BuildUnsecuredIniRequest(
            EbicsVersion.H004, "WSHOST", "WSPART", "WSUSER",
            rsaKey: E2EKeyPool.Subscriber(KeyPurpose.Signature));
}
