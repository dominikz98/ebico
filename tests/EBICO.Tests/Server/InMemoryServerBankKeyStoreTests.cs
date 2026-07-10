using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="InMemoryServerBankKeyStore"/> (issue #28): lazy generation and caching of the
/// bank key pair per host, and seeding via <see cref="IServerBankKeyStore.SetAsync"/>.
/// </summary>
public class InMemoryServerBankKeyStoreTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetOrCreate_IsStablePerHost_WithDefaultVersionsAndPrivateKeys()
    {
        var store = new InMemoryServerBankKeyStore();
        var host = HostId.Create("EBICOHOST");

        var first = await store.GetOrCreateAsync(host, _ct);
        var second = await store.GetOrCreateAsync(host, _ct);

        // The pair is generated once and cached, so HPB returns the same keys on every call.
        second.Should().BeSameAs(first);
        first.AuthenticationVersion.Value.Should().Be("X002");
        first.EncryptionVersion.Value.Should().Be("E002");
        first.Authentication.HasPrivateKey.Should().BeTrue();
        first.Encryption.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreate_DiffersPerHost()
    {
        var store = new InMemoryServerBankKeyStore();

        var a = await store.GetOrCreateAsync(HostId.Create("HOSTA"), _ct);
        var b = await store.GetOrCreateAsync(HostId.Create("HOSTB"), _ct);

        b.Authentication.Modulus.ToArray().Should().NotEqual(a.Authentication.Modulus.ToArray());
    }

    [Fact]
    public async Task Set_OverridesGeneratedPair()
    {
        var store = new InMemoryServerBankKeyStore();
        var host = HostId.Create("EBICOHOST");
        var seeded = new BankKeyPair(
            RsaKeyMaterial.Generate(), KeyVersion.Create("X002"),
            RsaKeyMaterial.Generate(), KeyVersion.Create("E002"));

        await store.SetAsync(host, seeded, _ct);
        var got = await store.GetOrCreateAsync(host, _ct);

        got.Should().BeSameAs(seeded);
    }

    [Fact]
    public async Task Set_NullKeys_Throws()
    {
        var store = new InMemoryServerBankKeyStore();

        var act = async () => await store.SetAsync(HostId.Create("EBICOHOST"), null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
