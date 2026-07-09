using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Keys;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Crypto;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Connector.Onboarding;

/// <summary>Tests for the explicit subscriber key generation (issue #47).</summary>
public class SubscriberKeyGeneratorTests
{
    private static FakeTransport UnusedTransport()
        => new(_ => new EbicsHttpResponse { StatusCode = 200, Payload = ReadOnlyMemory<byte>.Empty });

    [Fact]
    public async Task Generate_StoresAllThreeSubscriberKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        using var provider = OnboardingTestHarness.BuildProvider(EbicsVersion.H005, UnusedTransport());
        var generator = provider.GetRequiredService<ISubscriberKeyGenerator>();
        var keys = provider.GetRequiredService<IKeyStore>();

        var set = await generator.GenerateAsync(ct: ct);

        set.SignatureKeyVersion.Should().Be("A005");
        set.AuthenticationKeyVersion.Should().Be("X002");
        set.EncryptionKeyVersion.Should().Be("E002");
        set.SignatureKeyDigest.Should().HaveCount(32);
        (await keys.ContainsAsync(KeyOwner.Subscriber, KeyPurpose.Signature, ct)).Should().BeTrue();
        (await keys.ContainsAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, ct)).Should().BeTrue();
        (await keys.ContainsAsync(KeyOwner.Subscriber, KeyPurpose.Encryption, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Generate_Again_WithoutOverwrite_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        using var provider = OnboardingTestHarness.BuildProvider(EbicsVersion.H005, UnusedTransport());
        var generator = provider.GetRequiredService<ISubscriberKeyGenerator>();
        await generator.GenerateAsync(ct: ct);

        var act = async () => await generator.GenerateAsync(ct: ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
    }

    [Fact]
    public async Task Generate_Again_WithOverwrite_ReplacesKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        using var provider = OnboardingTestHarness.BuildProvider(EbicsVersion.H005, UnusedTransport());
        var generator = provider.GetRequiredService<ISubscriberKeyGenerator>();
        var first = await generator.GenerateAsync(ct: ct);

        var second = await generator.GenerateAsync(new SubscriberKeyGenerationOptions { Overwrite = true }, ct);

        second.SignatureKeyDigest.Should().NotEqual(first.SignatureKeyDigest);
    }
}
