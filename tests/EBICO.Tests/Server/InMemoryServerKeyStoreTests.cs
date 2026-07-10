using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="InMemoryServerKeyStore"/> (issue #26): store/get/contains, purpose isolation,
/// overwrite and subscriber isolation.
/// </summary>
public class InMemoryServerKeyStoreTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static SubscriberKeyRef Ref(string user = "USER01") =>
        new(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create(user));

    private static StoredPublicKey SignatureKey(string version = "A005") =>
        new(RsaKeyMaterial.Generate().ToPublicOnly(), KeyVersion.Create(version));

    [Fact]
    public async Task StoreThenGet_ReturnsTheStoredKey()
    {
        var store = new InMemoryServerKeyStore();
        var key = SignatureKey();

        await store.StoreAsync(Ref(), key, _ct);

        var loaded = await store.GetAsync(Ref(), KeyPurpose.Signature, _ct);
        loaded.Should().BeSameAs(key);
    }

    [Fact]
    public async Task Contains_ReflectsWhetherAKeyIsHeld()
    {
        var store = new InMemoryServerKeyStore();

        (await store.ContainsAsync(Ref(), KeyPurpose.Signature, _ct)).Should().BeFalse();

        await store.StoreAsync(Ref(), SignatureKey(), _ct);

        (await store.ContainsAsync(Ref(), KeyPurpose.Signature, _ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Get_ForADifferentPurpose_ReturnsNull()
    {
        var store = new InMemoryServerKeyStore();
        await store.StoreAsync(Ref(), SignatureKey(), _ct);

        var loaded = await store.GetAsync(Ref(), KeyPurpose.Encryption, _ct);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Store_OverwritesTheKeyForTheSamePurpose()
    {
        var store = new InMemoryServerKeyStore();
        await store.StoreAsync(Ref(), SignatureKey(), _ct);
        var replacement = SignatureKey();

        await store.StoreAsync(Ref(), replacement, _ct);

        (await store.GetAsync(Ref(), KeyPurpose.Signature, _ct)).Should().BeSameAs(replacement);
    }

    [Fact]
    public async Task Keys_AreIsolatedPerSubscriber()
    {
        var store = new InMemoryServerKeyStore();
        var key = SignatureKey();
        await store.StoreAsync(Ref("USER01"), key, _ct);

        (await store.GetAsync(Ref("USER02"), KeyPurpose.Signature, _ct)).Should().BeNull();
        (await store.GetAsync(Ref("USER01"), KeyPurpose.Signature, _ct)).Should().BeSameAs(key);
    }

    [Fact]
    public async Task Store_NullKey_Throws()
    {
        var store = new InMemoryServerKeyStore();

        var act = async () => await store.StoreAsync(Ref(), null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
