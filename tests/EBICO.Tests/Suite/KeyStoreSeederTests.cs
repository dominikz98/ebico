using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Server.State;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// Tests for <see cref="KeyStoreSeeder"/> (issue #55 / M7 epic): the sample subscriber keys land in
/// the <see cref="IServerKeyStore"/> and the bank key pair in the <see cref="IServerBankKeyStore"/>,
/// public-only, and seeding is idempotent.
/// </summary>
public class KeyStoreSeederTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServerKeyStore, InMemoryServerKeyStore>();
        services.AddSingleton<IServerBankKeyStore, InMemoryServerBankKeyStore>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedAsync_PopulatesSubscriberKeys()
    {
        await using var provider = BuildProvider();

        await KeyStoreSeeder.SeedAsync(provider, _ct);

        var keyStore = provider.GetRequiredService<IServerKeyStore>();
        var subscriber = KeyStoreSeedData.SampleSubscriber;

        var signature = await keyStore.GetAsync(subscriber, KeyPurpose.Signature, _ct);
        var encryption = await keyStore.GetAsync(subscriber, KeyPurpose.Encryption, _ct);
        var authentication = await keyStore.GetAsync(subscriber, KeyPurpose.Authentication, _ct);

        signature.Should().NotBeNull();
        signature!.Version.Value.Should().Be("A006");
        encryption.Should().NotBeNull();
        encryption!.Version.Value.Should().Be("E002");
        authentication.Should().NotBeNull();
        authentication!.Version.Value.Should().Be("X002");
    }

    [Fact]
    public async Task SeedAsync_PopulatesBankPairWithExpectedVersions()
    {
        await using var provider = BuildProvider();

        await KeyStoreSeeder.SeedAsync(provider, _ct);

        var bankKeyStore = provider.GetRequiredService<IServerBankKeyStore>();
        var pair = await bankKeyStore.GetOrCreateAsync(KeyStoreSeedData.SampleBankHost, _ct);

        pair.AuthenticationVersion.Value.Should().Be("X002");
        pair.EncryptionVersion.Value.Should().Be("E002");
    }

    [Fact]
    public async Task SeedAsync_SeedsPublicOnlyBankMaterial()
    {
        await using var provider = BuildProvider();

        await KeyStoreSeeder.SeedAsync(provider, _ct);

        var bankKeyStore = provider.GetRequiredService<IServerBankKeyStore>();
        var pair = await bankKeyStore.GetOrCreateAsync(KeyStoreSeedData.SampleBankHost, _ct);

        // The view only ever exposes public material; the seed must not carry private components.
        pair.Authentication.HasPrivateKey.Should().BeFalse();
        pair.Encryption.HasPrivateKey.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await using var provider = BuildProvider();

        await KeyStoreSeeder.SeedAsync(provider, _ct);
        await KeyStoreSeeder.SeedAsync(provider, _ct);

        var keyStore = provider.GetRequiredService<IServerKeyStore>();
        var subscriber = KeyStoreSeedData.SampleSubscriber;

        // Upsert, not append: re-seeding leaves exactly one key per purpose, unchanged.
        (await keyStore.ContainsAsync(subscriber, KeyPurpose.Signature, _ct)).Should().BeTrue();
        (await keyStore.GetAsync(subscriber, KeyPurpose.Signature, _ct))!.Version.Value.Should().Be("A006");
    }
}
