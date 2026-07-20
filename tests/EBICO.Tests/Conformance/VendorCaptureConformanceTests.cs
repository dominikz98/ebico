extern alias EbicoServer;
using System.Net;
using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Tests.Server;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EBICO.Tests.Conformance;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// Conformance (issue #59), the <b>real third-party client</b> layer: replays request XML captured from
/// <c>ebics-client</c> (github.com/node-ebics/node-ebics-client, MIT, speaking the H004 wire — see
/// <c>tools/vendor-capture/</c>) against the real server pipeline. The bytes were produced by a foreign
/// implementation, not EBICO's own connector, so this is the layer that can find a wire-format assumption
/// EBICO and its connector share but a real client does not.
/// </summary>
/// <remarks>
/// <para>
/// And it does. All three onboarding requests are currently <b>rejected</b> for one shared reason:
/// node-ebics-client emits <c>&lt;OrderDetails&gt;</c> <b>without an <c>xsi:type</c></b> discriminator (its
/// schema type is concrete), whereas EBICO's generated H004 binding types <c>OrderDetails</c> as the
/// <em>abstract</em> <c>OrderDetailsType</c> and needs the discriminator its own connector always emits.
/// The server therefore cannot deserialize a real client's request and answers with
/// <c>061099 EBICS_INTERNAL_ERROR</c>. This is exactly the class of interop gap that EBICO↔EBICO testing
/// (#57) is structurally blind to (both sides emit the discriminator), and it is the headline finding of
/// this milestone — see <c>docs/development/conformance-real-clients.md</c> for the analysis and the
/// recommended follow-up (type <c>OrderDetails</c> concretely; and map a client's undeserializable XML to
/// a client-error code rather than an internal-error one).
/// </para>
/// <para>
/// This is a <b>characterization</b> test: it pins the current, imperfect behaviour so that fixing either
/// half of the gap breaks it and forces the deviation doc to be updated. The captures live under
/// <c>tests/EBICO.Tests/Conformance/Vendor/</c> and are committed (an OSS client's output is not EBICS-SC
/// property — <c>docs/adr/0026-konformitaet-gegen-reale-clients.md</c>); on a checkout without them the
/// test skips, keeping the suite green.
/// </para>
/// </remarks>
public class VendorCaptureConformanceTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private const string Client = "node-ebics-client";
    private const EbicsVersion Version = EbicsVersion.H004;

    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public VendorCaptureConformanceTests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>The captured onboarding requests, in sequence order.</summary>
    public static TheoryData<string> OnboardingRequests => ["ini.xml", "hia.xml", "hpb.xml"];

    [Theory]
    [MemberData(nameof(OnboardingRequests))]
    public async Task NodeEbicsClient_H004_OnboardingRequest_IsRejected_BecauseOrderDetailsHasNoXsiType(string fileName)
    {
        if (!VendorCaptureCorpus.TryLoad(Client, Version, VendorDirection.Request, fileName, out var xml))
        {
            Assert.Skip(
                $"No vendor capture '{Client}/{Version}/request/{fileName}'. Generate it with " +
                "tools/vendor-capture/ — see docs/development/conformance-real-clients.md.");
        }

        // No seeding: the request fails to deserialize before the pipeline resolves a subscriber, so this
        // is purely about whether the server can parse a real client's wire format.
        var response = await _factory.CreateClient()
            .PostAsync("/ebics", new StringContent(xml, Encoding.UTF8, "text/xml"), _ct);

        // The server degrades gracefully to a well-formed EBICS error response (HTTP 200, parseable) rather
        // than a transport-level failure...
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));

        // ...but the outcome is a rejection: EBICO cannot deserialize <OrderDetails> without the xsi:type
        // discriminator the real client omits, currently surfaced (technical, header) as 061099. Guarded so
        // that fixing the binding — or the error classification — forces the deviation doc to be revisited.
        ServerTestHelpers.ReadReturnCodes(envelope).HeaderCode.Should().Be(EbicsReturnCode.InternalError.Code);
    }
}
