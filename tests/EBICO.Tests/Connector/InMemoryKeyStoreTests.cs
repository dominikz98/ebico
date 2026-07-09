using System.Security.Cryptography;
using AwesomeAssertions;
using EBICO.Connector.Keys;
using EBICO.Core.Crypto;

namespace EBICO.Tests.Connector;

/// <summary>Tests for <see cref="InMemoryKeyStore"/> round-trips and key identity.</summary>
public class InMemoryKeyStoreTests
{
    private static RsaKeyMaterial NewKey()
    {
        using var rsa = RSA.Create(2048);
        return RsaKeyMaterial.FromKeyPair(rsa);
    }

    [Fact]
    public async Task Store_Then_Get_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryKeyStore();
        var key = NewKey();

        await store.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Signature, key, ct);

        (await store.GetAsync(KeyOwner.Subscriber, KeyPurpose.Signature, ct)).Should().BeSameAs(key);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryKeyStore();

        (await store.GetAsync(KeyOwner.Bank, KeyPurpose.Encryption, ct)).Should().BeNull();
    }

    [Fact]
    public async Task Contains_ReflectsStoredState()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryKeyStore();

        (await store.ContainsAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, ct)).Should().BeFalse();
        await store.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, NewKey(), ct);
        (await store.ContainsAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Store_Overwrites_Existing()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryKeyStore();
        var first = NewKey();
        var second = NewKey();

        await store.StoreAsync(KeyOwner.Bank, KeyPurpose.Encryption, first, ct);
        await store.StoreAsync(KeyOwner.Bank, KeyPurpose.Encryption, second, ct);

        (await store.GetAsync(KeyOwner.Bank, KeyPurpose.Encryption, ct)).Should().BeSameAs(second);
    }

    [Fact]
    public async Task OwnerAndPurpose_FormDistinctKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryKeyStore();
        var subscriber = NewKey();
        var bank = NewKey();

        await store.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Encryption, subscriber, ct);
        await store.StoreAsync(KeyOwner.Bank, KeyPurpose.Encryption, bank, ct);

        (await store.GetAsync(KeyOwner.Subscriber, KeyPurpose.Encryption, ct)).Should().BeSameAs(subscriber);
        (await store.GetAsync(KeyOwner.Bank, KeyPurpose.Encryption, ct)).Should().BeSameAs(bank);
    }

    [Fact]
    public async Task Store_NullMaterial_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryKeyStore();

        var act = async () => await store.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Signature, null!, ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
