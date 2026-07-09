using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Keys;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Crypto;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Connector.Onboarding;

/// <summary>Tests for the HPB handler: signing, decryption, fingerprint verification and storage (issue #47).</summary>
public class HpbHandlerTests
{
    public static TheoryData<EbicsVersion> Versions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    private sealed record Fixture(
        ServiceProvider Provider, FakeTransport Transport, IKeyStore Keys, RsaKeyMaterial SubscriberEncryption);

    private static async Task<Fixture> ArrangeAsync(EbicsVersion version, Func<EbicsHttpResponse> responder, CancellationToken ct)
    {
        var transport = new FakeTransport(_ => responder());
        var provider = OnboardingTestHarness.BuildProvider(version, transport);
        await provider.GetRequiredService<ISubscriberKeyGenerator>().GenerateAsync(ct: ct);
        var keys = provider.GetRequiredService<IKeyStore>();
        var subEnc = (await keys.GetAsync(KeyOwner.Subscriber, KeyPurpose.Encryption, ct))!;
        return new Fixture(provider, transport, keys, subEnc);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Hpb_Succeeds_VerifiesAndStoresBankKeys(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] response = [];
        var fx = await ArrangeAsync(version, () => new EbicsHttpResponse { StatusCode = 200, Payload = response }, ct);
        using var provider = fx.Provider;

        var encVersion = KeyVersions.Default(KeyPurpose.Encryption, version).Version;
        var bankAuth = RsaKeyMaterial.Generate();
        var bankEnc = RsaKeyMaterial.Generate();
        response = OnboardingTestHarness.HpbResponse(
            version, fx.SubscriberEncryption, encVersion, bankAuth, bankEnc, "X002", "E002", "HOST");

        var result = await provider.GetRequiredService<IEbicsClient>().Send(
            new HpbRequest
            {
                ExpectedAuthenticationKeyDigest = new ReadOnlyMemory<byte>(PublicKeyFingerprint.Compute(bankAuth)),
                ExpectedEncryptionKeyDigest = new ReadOnlyMemory<byte>(PublicKeyFingerprint.Compute(bankEnc)),
            },
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FingerprintsVerified.Should().BeTrue();
        result.Value.BankKeys.HostId.Should().Be("HOST");
        result.Value.AuthenticationKeyDigest.Should().Equal(PublicKeyFingerprint.Compute(bankAuth));

        var storedAuth = (await fx.Keys.GetAsync(KeyOwner.Bank, KeyPurpose.Authentication, ct))!;
        var storedEnc = (await fx.Keys.GetAsync(KeyOwner.Bank, KeyPurpose.Encryption, ct))!;
        storedAuth.Modulus.ToArray().Should().Equal(bankAuth.Modulus.ToArray());
        storedEnc.Modulus.ToArray().Should().Equal(bankEnc.Modulus.ToArray());
    }

    [Fact]
    public async Task Hpb_FingerprintMismatch_Throws_AndDoesNotStore()
    {
        var ct = TestContext.Current.CancellationToken;
        const EbicsVersion version = EbicsVersion.H005;
        byte[] response = [];
        var fx = await ArrangeAsync(version, () => new EbicsHttpResponse { StatusCode = 200, Payload = response }, ct);
        using var provider = fx.Provider;

        var encVersion = KeyVersions.Default(KeyPurpose.Encryption, version).Version;
        var bankAuth = RsaKeyMaterial.Generate();
        var bankEnc = RsaKeyMaterial.Generate();
        response = OnboardingTestHarness.HpbResponse(
            version, fx.SubscriberEncryption, encVersion, bankAuth, bankEnc, "X002", "E002", "HOST");

        // Expect a fingerprint that does not match the received bank authentication key.
        var wrongDigest = PublicKeyFingerprint.Compute(RsaKeyMaterial.Generate());

        var act = async () => await provider.GetRequiredService<IEbicsClient>().Send(
            new HpbRequest { ExpectedAuthenticationKeyDigest = new ReadOnlyMemory<byte>(wrongDigest) }, ct);

        await act.Should().ThrowAsync<EbicsOnboardingException>();
        (await fx.Keys.ContainsAsync(KeyOwner.Bank, KeyPurpose.Authentication, ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Hpb_BusinessReturnCode_YieldsFailure_AndDoesNotStore()
    {
        var ct = TestContext.Current.CancellationToken;
        const EbicsVersion version = EbicsVersion.H005;
        var response = OnboardingTestHarness.KeyManagementResponse(version, "091002");
        var fx = await ArrangeAsync(version, () => new EbicsHttpResponse { StatusCode = 200, Payload = response }, ct);
        using var provider = fx.Provider;

        var result = await provider.GetRequiredService<IEbicsClient>().Send(new HpbRequest(), ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be("091002");
        (await fx.Keys.ContainsAsync(KeyOwner.Bank, KeyPurpose.Authentication, ct)).Should().BeFalse();
    }
}
