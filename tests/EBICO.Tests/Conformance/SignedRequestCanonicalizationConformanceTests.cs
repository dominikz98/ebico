extern alias EbicoServer;
using System.Net;
using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Server.State;
using EBICO.Tests.E2E;
using EBICO.Tests.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Conformance;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// Conformance (issue #59), <b>signed-request canonicalization</b> layer. A third-party client is free to
/// sign with either inclusive or exclusive XML canonicalization; its <c>SignedInfo</c> declares which via
/// the <c>CanonicalizationMethod</c>/<c>Transform</c> algorithm URIs. This suite signs the same request
/// both ways and asserts the server verifies it either way — proving the X002 verifier reads the
/// algorithm from the message (<c>C14nAlgorithms.FromAlgorithmUri</c>) rather than assuming EBICO's own
/// default.
/// </summary>
/// <remarks>
/// The honest limit (spelled out in <c>docs/development/conformance-real-clients.md</c>): this proves
/// algorithm-URI <em>adaptivity</em>, not that EBICO's canonical octets are byte-identical to a foreign
/// library's for the same algorithm. Because EBICO signs and verifies with the same code, the round-trip
/// is self-consistent; the deeper octet-level interop question is what the committed vendor captures
/// (<see cref="VendorCaptureConformanceTests"/>) exist to answer.
/// </remarks>
public class SignedRequestCanonicalizationConformanceTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public SignedRequestCanonicalizationConformanceTests(WebApplicationFactory<ServerProgram> factory)
        => _factory = factory;

    /// <summary>Each supported version signed with each client-selectable canonicalization variant.</summary>
    public static TheoryData<EbicsVersion, C14nMode> Cases
    {
        get
        {
            var data = new TheoryData<EbicsVersion, C14nMode>();
            foreach (var version in (EbicsVersion[])[EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005])
            {
                foreach (var mode in (C14nMode[])[C14nMode.Inclusive, C14nMode.Exclusive])
                {
                    data.Add(version, mode);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SignedRequest_WithClientChosenCanonicalization_IsVerifiedServerSide(
        EbicsVersion version, C14nMode c14n)
    {
        // Isolated host so the seeded subscriber and its suspension stay local to this case.
        var factory = _factory.WithWebHostBuilder(_ => { });
        var scenario = $"{version}{c14n}";
        var host = HostId.Create($"C14N{scenario}");
        var partner = PartnerId.Create($"P{scenario}");
        var user = UserId.Create($"U{scenario}");

        var master = factory.Services.GetRequiredService<IMasterDataManager>();
        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);

        // Seed the subscriber's X002 authentication key so the server's X002 verifier has a key to verify
        // against (normally delivered by HIA). The same key pair signs the request below.
        var authKey = E2EKeyPool.Subscriber(KeyPurpose.Authentication);
        var authVersion = KeyVersions.Default(KeyPurpose.Authentication, version).Version;
        await factory.Services.GetRequiredService<IServerKeyStore>().StoreAsync(
            new SubscriberKeyRef(host, partner, user),
            new StoredPublicKey(authKey.ToPublicOnly(), authVersion),
            _ct);

        // A signed single-phase SPR request, canonicalized the way a third-party client chose.
        var unsigned = ServerTestHelpers.BuildSprRequest(version, host.Value, partner.Value, user.Value);
        var signed = ServerTestHelpers.SignRequestXml(version, unsigned, authKey, c14n);

        var response = await factory.CreateClient().PostAsync(
            "/ebics", new StringContent(signed, Encoding.UTF8, "text/xml"), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);

        // The linchpin: the server did NOT reject with 061001 AUTHENTICATION_FAILED for either
        // canonicalization. The suspension is the definitive proof that the pipeline reached the SPR
        // handler — which happens only once X002 verification passed.
        headerCode.Should().NotBe(EbicsReturnCode.AuthenticationFailed.Code);
        bodyCode.Should().NotBe(EbicsReturnCode.AuthenticationFailed.Code);
        (await master.GetSubscriberAsync(host, partner, user, _ct))!.State.Should().Be(SubscriberState.Suspended);
    }
}
