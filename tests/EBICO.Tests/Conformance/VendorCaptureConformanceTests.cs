extern alias EbicoServer;
using System.Net;
using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.State;
using EBICO.Tests.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using H004 = EBICO.Core.Schema.H004;

namespace EBICO.Tests.Conformance;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// Conformance (issues #59/#117), the <b>real third-party client</b> layer: replays request XML captured
/// from <c>ebics-client</c> (github.com/node-ebics/node-ebics-client, MIT, speaking the H004 wire — see
/// <c>tools/vendor-capture/</c>) against the real server pipeline. The bytes were produced by a foreign
/// implementation, not EBICO's own connector, so this is the layer that can find a wire-format assumption
/// EBICO and its connector share but a real client does not.
/// </summary>
/// <remarks>
/// <para>
/// It found exactly one, and #117 fixed it: node-ebics-client emits <c>&lt;OrderDetails&gt;</c>
/// <b>without an <c>xsi:type</c></b> discriminator (its schema type is concrete), whereas EBICO's
/// generated binding typed <c>OrderDetails</c> as the <em>abstract</em> <c>OrderDetailsType</c> and
/// needed the discriminator its own connector always emitted — so every onboarding request of a real
/// client was rejected. It also signs its INI order data with <c>A006</c>/PSS on H004, which EBICO
/// permitted for H005 only. Both are fixed; see <c>docs/development/conformance-real-clients.md</c> and
/// ADR-0029.
/// </para>
/// <para>
/// This test is therefore no longer a characterization of a defect but the <b>positive</b> interop proof:
/// a foreign client's captured octets drive the subscriber lifecycle <c>New → Initialized → Ready</c> and
/// return the bank's encrypted public keys. It is a single sequential test on purpose — the three
/// captures are one chain and each step is a precondition of the next. The captures live under
/// <c>tests/EBICO.Tests/Conformance/Vendor/</c> and are committed (an OSS client's output is not EBICS-SC
/// property — <c>docs/adr/0026-konformitaet-gegen-reale-clients.md</c>); on a checkout without them the
/// test skips, keeping the suite green.
/// </para>
/// <para>
/// What it still does <b>not</b> prove: the HPB capture carries a foreign X002 <c>AuthSignature</c>, but
/// <c>X002EbicsRequestVerifier</c> skips <c>ebicsNoPubKeyDigestsRequest</c> (it bootstraps the key
/// exchange), so EBICO's canonicalization is not compared against a foreign signer's octets here.
/// </para>
/// </remarks>
public class VendorCaptureConformanceTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private const string Client = "node-ebics-client";
    private const EbicsVersion Version = EbicsVersion.H004;

    // The placeholder identifiers baked into the captures; see the corpus PROVENANCE.md.
    private const string CaptureHostId = "EBICOHOST";
    private const string CapturePartnerId = "PARTNER1";
    private const string CaptureUserId = "USER1";

    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public VendorCaptureConformanceTests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    [Fact]
    public async Task NodeEbicsClient_H004_Onboarding_DrivesSubscriberToReady()
    {
        var captures = LoadCapturesOrSkip();

        var master = _factory.Services.GetRequiredService<IMasterDataManager>();
        var hostId = HostId.Create(CaptureHostId);
        var partnerId = PartnerId.Create(CapturePartnerId);
        var userId = UserId.Create(CaptureUserId);

        // Master data only — no keys: the captured INI/HIA carry the client's own throwaway key material
        // and must be what registers it, otherwise the replay would prove nothing about onboarding.
        await master.SaveBankAsync(new Bank(hostId), _ct);
        await master.SavePartnerAsync(new Partner(hostId, partnerId), _ct);
        await master.SaveSubscriberAsync(new Subscriber(hostId, partnerId, userId), _ct);

        // INI: the A006/PSS signature key. New -> Initialized.
        (await PostAsync(captures.Ini)).Should().Be(("000000", "000000"));
        await AssertStateAsync(master, hostId, partnerId, userId, SubscriberState.Initialized);

        // HIA: the X002 authentication and E002 encryption keys. Initialized -> Ready.
        (await PostAsync(captures.Hia)).Should().Be(("000000", "000000"));
        await AssertStateAsync(master, hostId, partnerId, userId, SubscriberState.Ready);

        // HPB: the bank's public keys, encrypted for the E002 key HIA just registered.
        var hpb = await PostEnvelopeAsync(captures.Hpb);
        ServerTestHelpers.ReadReturnCodes(hpb).Should().Be(("000000", "000000"));
        hpb.Should().BeOfType<H004.EbicsKeyManagementResponse>()
            .Which.Body?.DataTransfer?.OrderData?.Value.Should()
            .NotBeNullOrEmpty("HPB must answer with the encrypted bank public keys");
    }

    private (string Ini, string Hia, string Hpb) LoadCapturesOrSkip()
    {
        var loaded = new Dictionary<string, string>();
        foreach (var fileName in (string[])["ini.xml", "hia.xml", "hpb.xml"])
        {
            if (!VendorCaptureCorpus.TryLoad(Client, Version, VendorDirection.Request, fileName, out var xml))
            {
                Assert.Skip(
                    $"No vendor capture '{Client}/{Version}/request/{fileName}'. Generate it with " +
                    "tools/vendor-capture/ — see docs/development/conformance-real-clients.md.");
            }

            loaded[fileName] = xml;
        }

        return (loaded["ini.xml"], loaded["hia.xml"], loaded["hpb.xml"]);
    }

    private async Task<(string? HeaderCode, string? BodyCode)> PostAsync(string requestXml)
        => ServerTestHelpers.ReadReturnCodes(await PostEnvelopeAsync(requestXml));

    private async Task<IEbicsEnvelope> PostEnvelopeAsync(string requestXml)
    {
        var response = await _factory.CreateClient()
            .PostAsync("/ebics", new StringContent(requestXml, Encoding.UTF8, "text/xml"), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
    }

    private async Task AssertStateAsync(
        IMasterDataManager master, HostId hostId, PartnerId partnerId, UserId userId, SubscriberState expected)
    {
        var subscriber = await master.GetSubscriberAsync(hostId, partnerId, userId, _ct);
        subscriber!.State.Should().Be(expected);
    }
}
