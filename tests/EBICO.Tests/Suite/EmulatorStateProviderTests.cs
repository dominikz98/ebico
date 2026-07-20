using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server.State;
using EBICO.Suite.Services;

namespace EBICO.Tests.Suite;

/// <summary>
/// Tests for the live <see cref="EmulatorStateProvider"/> read-model bridge (issues #53/#55): banks,
/// partners and subscribers come from the server-side <see cref="IEbicsStateStore"/>, and the public
/// keys from <see cref="IServerKeyStore"/> (subscriber keys) and <see cref="IServerBankKeyStore"/>
/// (the bank key pair).
/// </summary>
public class EmulatorStateProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static EmulatorStateProvider CreateSut(
        IEbicsStateStore store,
        IServerKeyStore? keyStore = null,
        IServerBankKeyStore? bankKeyStore = null)
        => new(store, keyStore ?? new InMemoryServerKeyStore(), bankKeyStore ?? new InMemoryServerBankKeyStore());

    [Fact]
    public async Task Reads_Banks_Partners_Subscribers_FromStore()
    {
        var store = new InMemoryEbicsStateStore();
        await store.RegisterBankAsync(new Bank(HostId.Create("HOSTA")), _ct);
        await store.RegisterPartnerAsync(new Partner(HostId.Create("HOSTA"), PartnerId.Create("CUST01")), _ct);
        await store.RegisterSubscriberAsync(
            new Subscriber(HostId.Create("HOSTA"), PartnerId.Create("CUST01"), UserId.Create("USER01")), _ct);

        var sut = CreateSut(store);

        (await sut.GetBanksAsync(_ct)).Should().ContainSingle(b => b.HostId.Value == "HOSTA");
        (await sut.GetPartnersAsync(_ct)).Should().ContainSingle(p => p.PartnerId.Value == "CUST01");
        (await sut.GetSubscribersAsync(_ct)).Should().ContainSingle(s => s.UserId.Value == "USER01");
    }

    [Fact]
    public async Task Reflects_LiveStoreMutations()
    {
        var store = new InMemoryEbicsStateStore();
        var sut = CreateSut(store);

        (await sut.GetBanksAsync(_ct)).Should().BeEmpty();

        await store.RegisterBankAsync(new Bank(HostId.Create("HOSTB")), _ct);

        (await sut.GetBanksAsync(_ct)).Should().ContainSingle(b => b.HostId.Value == "HOSTB");
    }

    [Fact]
    public async Task GetKeysAsync_ReadsSeededSubscriberAndBankKeys()
    {
        var store = new InMemoryEbicsStateStore();
        var keyStore = new InMemoryServerKeyStore();
        var bankKeyStore = new InMemoryServerBankKeyStore();

        // Register the sample subscriber and seed the key stores exactly as the app does at start-up.
        var subscriberRef = KeyStoreSeedData.SampleSubscriber;
        await store.RegisterBankAsync(new Bank(subscriberRef.HostId), _ct);
        await store.RegisterPartnerAsync(new Partner(subscriberRef.HostId, subscriberRef.PartnerId), _ct);
        await store.RegisterSubscriberAsync(
            new Subscriber(subscriberRef.HostId, subscriberRef.PartnerId, subscriberRef.UserId), _ct);

        foreach (var (subscriber, key) in KeyStoreSeedData.SubscriberKeys)
        {
            await keyStore.StoreAsync(subscriber, key, _ct);
        }

        foreach (var (host, pair) in KeyStoreSeedData.BankKeys)
        {
            await bankKeyStore.SetAsync(host, pair, _ct);
        }

        var sut = new EmulatorStateProvider(store, keyStore, bankKeyStore);

        var keys = await sut.GetKeysAsync(_ct);

        keys.Should().HaveCount(5);
        keys.Should().Contain(k => k.OwnerLabel == "Teilnehmer PARTNER01 / USER0001")
            .And.Contain(k => k.OwnerLabel == "Bank EBICOHOST");
        keys.Select(k => k.KeyVersion).Should().Contain("A006").And.Contain("E002").And.Contain("X002");

        foreach (var key in keys)
        {
            var expected = PublicKeyFingerprint.ToLetterFormat(PublicKeyFingerprint.Compute(key.PublicKey));
            key.FingerprintText.Should().Be(expected);
        }
    }

    [Fact]
    public async Task GetKeysAsync_OnlySurfacesStoredSubscriberKeysAndSeededBankHosts()
    {
        var store = new InMemoryEbicsStateStore();
        await store.RegisterBankAsync(new Bank(HostId.Create("HOSTA")), _ct);
        await store.RegisterPartnerAsync(new Partner(HostId.Create("HOSTA"), PartnerId.Create("CUST01")), _ct);
        await store.RegisterSubscriberAsync(
            new Subscriber(HostId.Create("HOSTA"), PartnerId.Create("CUST01"), UserId.Create("USER01")), _ct);

        var sut = CreateSut(store);

        var keys = await sut.GetKeysAsync(_ct);

        // A subscriber present in the store but without stored keys yields no key view: the list is
        // driven by the per-purpose GetAsync probe, not by enumeration.
        keys.Should().NotContain(k => k.OwnerLabel.Contains("Teilnehmer"));
        // A bank present in the store but not among the seeded BankHosts is never surfaced.
        keys.Should().NotContain(k => k.OwnerLabel == "Bank HOSTA");
    }
}
