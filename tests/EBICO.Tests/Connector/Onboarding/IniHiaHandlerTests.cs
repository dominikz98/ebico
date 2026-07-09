using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Crypto;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Connector.Onboarding;

/// <summary>
/// Round-trip tests for the INI and HIA handlers across all supported versions (issue #47): the
/// request is built, sent through a fake transport, and the embedded order data is decompressed and
/// re-parsed to confirm the correct keys were serialized.
/// </summary>
public class IniHiaHandlerTests
{
    public static TheoryData<EbicsVersion> Versions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Ini_Succeeds_AndEmbedsSignatureKey(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = OnboardingTestHarness.KeyManagementResponse(version, "000000");
        var transport = new FakeTransport(_ => new EbicsHttpResponse { StatusCode = 200, Payload = response });
        using var provider = OnboardingTestHarness.BuildProvider(version, transport);
        await provider.GetRequiredService<ISubscriberKeyGenerator>().GenerateAsync(ct: ct);
        var client = provider.GetRequiredService<IEbicsClient>();

        var result = await client.Send(new IniRequest(), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SignatureKeyVersion.Should().Be("A005");
        result.Value.Letter.Should().NotBeNull();
        result.Value.Letter!.Pdf.Should().NotBeNull();

        var orderData = OnboardingTestHarness.DecompressedUnsecuredOrderData(version, transport.LastRequestPayload!);
        var embeddedKey = OnboardingTestHarness.IniSignatureKey(version, orderData);
        PublicKeyFingerprint.Compute(embeddedKey).Should().Equal(result.Value.SignatureKeyDigest);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Hia_Succeeds_AndEmbedsAuthenticationAndEncryptionKeys(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = OnboardingTestHarness.KeyManagementResponse(version, "000000");
        var transport = new FakeTransport(_ => new EbicsHttpResponse { StatusCode = 200, Payload = response });
        using var provider = OnboardingTestHarness.BuildProvider(version, transport);
        await provider.GetRequiredService<ISubscriberKeyGenerator>().GenerateAsync(ct: ct);
        var client = provider.GetRequiredService<IEbicsClient>();

        var result = await client.Send(new HiaRequest(), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Letter.Should().NotBeNull();

        var orderData = OnboardingTestHarness.DecompressedUnsecuredOrderData(version, transport.LastRequestPayload!);
        var (auth, enc) = OnboardingTestHarness.HiaKeys(version, orderData);
        PublicKeyFingerprint.Compute(auth).Should().Equal(result.Value.AuthenticationKeyDigest);
        PublicKeyFingerprint.Compute(enc).Should().Equal(result.Value.EncryptionKeyDigest);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Ini_BusinessReturnCode_YieldsFailure(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = OnboardingTestHarness.KeyManagementResponse(version, "091002");
        var transport = new FakeTransport(_ => new EbicsHttpResponse { StatusCode = 200, Payload = response });
        using var provider = OnboardingTestHarness.BuildProvider(version, transport);
        await provider.GetRequiredService<ISubscriberKeyGenerator>().GenerateAsync(ct: ct);
        var client = provider.GetRequiredService<IEbicsClient>();

        var result = await client.Send(new IniRequest(), ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be("091002");
    }

    [Fact]
    public async Task Ini_WithoutGeneratedKeys_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = OnboardingTestHarness.KeyManagementResponse(EbicsVersion.H005, "000000");
        var transport = new FakeTransport(_ => new EbicsHttpResponse { StatusCode = 200, Payload = response });
        using var provider = OnboardingTestHarness.BuildProvider(EbicsVersion.H005, transport);
        var client = provider.GetRequiredService<IEbicsClient>();

        var act = async () => await client.Send(new IniRequest(), ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
    }
}
