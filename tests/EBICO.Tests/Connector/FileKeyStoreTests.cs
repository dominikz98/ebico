using System.Security.Cryptography;
using AwesomeAssertions;
using EBICO.Connector.Keys;
using EBICO.Core.Crypto;

namespace EBICO.Tests.Connector;

/// <summary>
/// Tests for <see cref="FileKeyStore"/>: PKCS#8 (subscriber, private) and SubjectPublicKeyInfo
/// (bank, public) round-trips over a temporary directory.
/// </summary>
public sealed class FileKeyStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ebico-keystore-tests", Guid.NewGuid().ToString("N"));

    private static RsaKeyMaterial NewKeyPair()
    {
        using var rsa = RSA.Create(2048);
        return RsaKeyMaterial.FromKeyPair(rsa);
    }

    [Fact]
    public async Task Subscriber_PrivateKey_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileKeyStore(_directory);
        var key = NewKeyPair();

        await store.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Signature, key, ct);
        var loaded = await store.GetAsync(KeyOwner.Subscriber, KeyPurpose.Signature, ct);

        loaded.Should().NotBeNull();
        loaded!.HasPrivateKey.Should().BeTrue();
        loaded.Modulus.ToArray().Should().Equal(key.Modulus.ToArray());
        loaded.Exponent.ToArray().Should().Equal(key.Exponent.ToArray());
    }

    [Fact]
    public async Task Bank_PublicKey_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileKeyStore(_directory);
        var key = NewKeyPair().ToPublicOnly();

        await store.StoreAsync(KeyOwner.Bank, KeyPurpose.Encryption, key, ct);
        var loaded = await store.GetAsync(KeyOwner.Bank, KeyPurpose.Encryption, ct);

        loaded.Should().NotBeNull();
        loaded!.HasPrivateKey.Should().BeFalse();
        loaded.Modulus.ToArray().Should().Equal(key.Modulus.ToArray());
        loaded.Exponent.ToArray().Should().Equal(key.Exponent.ToArray());
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileKeyStore(_directory);

        (await store.GetAsync(KeyOwner.Subscriber, KeyPurpose.Signature, ct)).Should().BeNull();
    }

    [Fact]
    public async Task Contains_ReflectsStoredState()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileKeyStore(_directory);

        (await store.ContainsAsync(KeyOwner.Bank, KeyPurpose.Authentication, ct)).Should().BeFalse();
        await store.StoreAsync(KeyOwner.Bank, KeyPurpose.Authentication, NewKeyPair().ToPublicOnly(), ct);
        (await store.ContainsAsync(KeyOwner.Bank, KeyPurpose.Authentication, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task StoreSubscriber_WithPublicOnlyKey_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new FileKeyStore(_directory);
        var publicOnly = NewKeyPair().ToPublicOnly();

        var act = async () => await store.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Signature, publicOnly, ct);

        await act.Should().ThrowAsync<KeyMaterialException>();
    }

    [Fact]
    public void Constructor_BlankDirectory_Throws()
    {
        var act = () => new FileKeyStore("   ");

        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
