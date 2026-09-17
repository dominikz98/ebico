extern alias EbicoServer;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Download;
using EBICO.Connector.Keys;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EBICO.Tests.E2E;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end evidence for the bank's X002 response signature (issue #143): the real server signs every
/// transaction <c>ebicsResponse</c> with the bank key the client fetched over HPB, and the real connector
/// verifies it before acting on the content.
/// </summary>
/// <remarks>
/// <para>
/// This is the round-trip the emulator previously could not survive against a signature-checking client:
/// onboarding (INI/HIA/HPB) went through because those responses are unsigned by protocol, and every
/// order after it failed. The negative case here reproduces exactly that by swapping the server's signer
/// for <see cref="UnsignedEbicsResponseSigner"/>.
/// </para>
/// <para>
/// The signature is asserted on what the <em>server</em> recorded it sent (the message capture) and
/// verified with the key the <em>client</em> holds — so neither side gets to mark its own homework.
/// </para>
/// </remarks>
public class ResponseSignatureE2ETests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public ResponseSignatureE2ETests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>The EBICS versions covered by the end-to-end matrix.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Every_transaction_response_is_signed_with_the_bank_key_the_client_fetched_over_hpb(
        EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(_factory, version, "SIGOK", ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var result = await harness.Client.Send(new C53DownloadRequest(), _ct);
        result.IsSuccess.Should().BeTrue($"C53 download failed: {result.ReturnCode} {result.ReturnText}");

        // The bank's public authentication key, as HPB left it in the connector's store.
        var bankAuthKey = await harness.ConnectorKeys.GetAsync(KeyOwner.Bank, KeyPurpose.Authentication, _ct);
        bankAuthKey.Should().NotBeNull("HPB delivers the bank's X002 key");

        var captured = await harness.ServerServices.GetRequiredService<IMessageCaptureStore>()
            .GetAsync(result.Value!.TransactionId, _ct);
        captured.Should().NotBeEmpty("the three-phase download records a message per phase");

        var authVersion = KeyVersions.Default(KeyPurpose.Authentication, version).Version;
        foreach (var message in captured)
        {
            var envelope = EbicsXmlSerializer.DeserializeEnvelope(message.ResponseXml!);
            var signed = envelope.Should().BeAssignableTo<IAuthSignedResponseEnvelope>().Subject;

            signed.AuthSignature.Should().NotBeNull(
                $"the {message.Phase} response must be signed");
            AuthenticationSignature.Verify(message.ResponseXml!, signed.AuthSignature, bankAuthKey!, authVersion)
                .Should().BeTrue($"the {message.Phase} response signature must verify against the bank key");
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Onboarding_succeeds_against_a_server_that_does_not_sign(EbicsVersion version)
    {
        // The pre-#143 behaviour, and the reason the gap went unnoticed for so long: INI/HIA/HPB carry no
        // AuthSignature by protocol, so onboarding is indifferent to whether the server signs at all.
        await using var harness = await EbicsE2EHarness.CreateAsync(
            WithUnsignedResponses(), version, "SIGOFFON", ct: _ct);

        var onboarding = await harness.OnboardAsync(_ct);

        // ThrowIfFailed asserts INI, HIA and HPB individually.
        onboarding.ThrowIfFailed();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Business_order_against_a_server_that_does_not_sign_is_rejected(EbicsVersion version)
    {
        // The FSM Cloud finding, reproduced: everything after onboarding fails on a client that verifies.
        await using var harness = await EbicsE2EHarness.CreateAsync(
            WithUnsignedResponses(), version, "SIGOFF", ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var act = async () => await harness.Client.Send(new C53DownloadRequest(), _ct);

        await act.Should().ThrowAsync<EbicsResponseSignatureException>();
    }

    // A server host whose response signer is the no-op one, for the negative cases. ConfigureTestServices
    // runs after AddEbicoServer, so the TryAdd default has to be replaced rather than pre-empted.
    private WebApplicationFactory<ServerProgram> WithUnsignedResponses()
        => _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(
                ServiceDescriptor.Singleton<IEbicsResponseSigner, UnsignedEbicsResponseSigner>())));
}
