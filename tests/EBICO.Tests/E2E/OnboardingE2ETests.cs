extern alias EbicoServer;
using AwesomeAssertions;
using EBICO.Connector.Keys;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.E2E;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end onboarding (issue #57): the real connector drives INI, HIA and HPB against the real
/// in-process server for every supported EBICS version. Unlike <c>OnboardingTestHarness</c> — which
/// answers with a hand-built fake bank response — nothing here stands in for the server, so the
/// subscriber state machine, the key exchange and the E002 round-trip are all genuine.
/// </summary>
public class OnboardingE2ETests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public OnboardingE2ETests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>The EBICS versions covered by the end-to-end matrix.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Onboarding_IniHiaHpb_DrivesSubscriberToReady_AndExchangesKeys(EbicsVersion version)
    {
        // provisionKeys: false — this is the one flow where key generation is the subject under test,
        // so it runs the real generator rather than the shared pool.
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "OB", provisionKeys: false, ct: _ct);
        var serverKeys = harness.ServerServices.GetRequiredService<IServerKeyStore>();

        var keySet = await harness.KeyGenerator.GenerateAsync(ct: _ct);
        keySet.SignatureKeyDigest.Should().NotBeEmpty();
        keySet.AuthenticationKeyDigest.Should().NotBeEmpty();
        keySet.EncryptionKeyDigest.Should().NotBeEmpty();

        // INI: the signature key (A00x) reaches the server and the subscriber advances New -> Initialized.
        var ini = await harness.Client.Send(new IniRequest { IncludeLetter = false }, _ct);
        ini.IsSuccess.Should().BeTrue($"INI failed: {ini.ReturnCode} {ini.ReturnText}");
        ini.ReturnCode.Should().Be(EbicsReturnCode.OkCode);
        (await harness.GetSubscriberAsync(_ct))!.State.Should().Be(SubscriberState.Initialized);

        // The key the server stored is the key the connector generated — proven over the wire, not by
        // comparing the connector to itself.
        var storedSignature = await serverKeys.GetAsync(harness.KeyRef, KeyPurpose.Signature, _ct);
        PublicKeyFingerprint.Compute(storedSignature!.Key).Should().Equal(ini.Value!.SignatureKeyDigest);

        // HIA: authentication and encryption keys reach the server; Initialized -> Ready.
        var hia = await harness.Client.Send(new HiaRequest { IncludeLetter = false }, _ct);
        hia.IsSuccess.Should().BeTrue($"HIA failed: {hia.ReturnCode} {hia.ReturnText}");
        hia.ReturnCode.Should().Be(EbicsReturnCode.OkCode);
        (await harness.GetSubscriberAsync(_ct))!.State.Should().Be(SubscriberState.Ready);

        var storedAuthentication = await serverKeys.GetAsync(harness.KeyRef, KeyPurpose.Authentication, _ct);
        PublicKeyFingerprint.Compute(storedAuthentication!.Key).Should().Equal(hia.Value!.AuthenticationKeyDigest);
        var storedEncryption = await serverKeys.GetAsync(harness.KeyRef, KeyPurpose.Encryption, _ct);
        PublicKeyFingerprint.Compute(storedEncryption!.Key).Should().Equal(hia.Value.EncryptionKeyDigest);

        // HPB: the bank keys come back E002-encrypted for the subscriber key HIA just delivered.
        var hpb = await harness.Client.Send(
            new HpbRequest
            {
                ExpectedAuthenticationKeyDigest = PublicKeyFingerprint.Compute(E2EKeyPool.BankKeys().Authentication),
                ExpectedEncryptionKeyDigest = PublicKeyFingerprint.Compute(E2EKeyPool.BankKeys().Encryption),
            },
            _ct);
        hpb.IsSuccess.Should().BeTrue($"HPB failed: {hpb.ReturnCode} {hpb.ReturnText}");
        hpb.ReturnCode.Should().Be(EbicsReturnCode.OkCode);

        // The linchpin of the whole suite. Reaching a verified true here proves two things at once: the
        // connector decrypted the E002 payload (otherwise there would be no bank keys to fingerprint) and
        // the keys inside were exactly the ones seeded on the server (a mismatch throws
        // EbicsOnboardingException). That closes the compress -> E002 -> wire -> decrypt loop. Asserting it
        // also guards against the check going vacuous: it is only true when expected digests were passed.
        hpb.Value!.FingerprintsVerified.Should().BeTrue();

        (await harness.ConnectorKeys.ContainsAsync(KeyOwner.Bank, KeyPurpose.Authentication, _ct)).Should().BeTrue();
        (await harness.ConnectorKeys.ContainsAsync(KeyOwner.Bank, KeyPurpose.Encryption, _ct)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Onboarding_OutOfOrder_IsRejected_AndLeavesSubscriberInNew(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(_factory, version, "OBNEG", ct: _ct);

        // HIA before INI: the server's state machine requires Initialized.
        var hia = await harness.Client.Send(new HiaRequest { IncludeLetter = false }, _ct);
        hia.IsSuccess.Should().BeFalse();
        hia.ReturnCode.Should().Be(EbicsReturnCode.InvalidUserOrUserState.Code);

        // HPB before onboarding: the server requires Ready.
        var hpb = await harness.Client.Send(new HpbRequest(), _ct);
        hpb.IsSuccess.Should().BeFalse();
        hpb.ReturnCode.Should().Be(EbicsReturnCode.InvalidUserOrUserState.Code);

        (await harness.GetSubscriberAsync(_ct))!.State.Should().Be(SubscriberState.New);
    }
}
