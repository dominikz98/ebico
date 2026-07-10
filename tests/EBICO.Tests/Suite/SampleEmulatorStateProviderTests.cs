using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Suite.Services;

namespace EBICO.Tests.Suite;

/// <summary>
/// Tests for the placeholder <see cref="SampleEmulatorStateProvider"/> that backs the
/// Suite UI grundgerüst (issue #52) until the real server store exists (M3/M4).
/// </summary>
public class SampleEmulatorStateProviderTests
{
    private readonly SampleEmulatorStateProvider _sut = new();

    [Fact]
    public async Task GetBanksAsync_ReturnsSeededBanks()
    {
        var banks = await _sut.GetBanksAsync(TestContext.Current.CancellationToken);

        banks.Should().HaveCount(2);
        banks.Select(b => b.HostId.Value).Should().Contain("EBICOHOST").And.Contain("BANKB");
    }

    [Fact]
    public async Task GetPartnersAsync_ReturnsSeededPartners()
    {
        var partners = await _sut.GetPartnersAsync(TestContext.Current.CancellationToken);

        partners.Should().HaveCount(3);
        partners.Select(p => p.PartnerId.Value).Should().Contain("PARTNER01").And.Contain("PARTNER02");
    }

    [Fact]
    public async Task GetPartnersAsync_CoversSamePartnerIdAtTwoBanks()
    {
        var partners = await _sut.GetPartnersAsync(TestContext.Current.CancellationToken);

        partners.Where(p => p.PartnerId.Value == "PARTNER02")
            .Select(p => p.HostId.Value)
            .Should().BeEquivalentTo(["EBICOHOST", "BANKB"], "the same partner id denotes distinct customers per bank");
    }

    [Fact]
    public async Task GetSubscribersAsync_ReturnsSeededSubscribers()
    {
        var subscribers = await _sut.GetSubscribersAsync(TestContext.Current.CancellationToken);

        subscribers.Should().HaveCount(4);
        subscribers.Should().OnlyContain(s => !string.IsNullOrEmpty(s.UserId.Value));
    }

    [Fact]
    public async Task Subscribers_OnlyReferenceKnownPartners()
    {
        var partners = (await _sut.GetPartnersAsync(TestContext.Current.CancellationToken))
            .Select(p => (p.HostId, p.PartnerId)).ToHashSet();

        var subscribers = await _sut.GetSubscribersAsync(TestContext.Current.CancellationToken);

        subscribers.Select(s => (s.HostId, s.PartnerId))
            .Should().OnlyContain(pair => partners.Contains(pair), "each subscriber belongs to a known (bank, partner)");
    }

    [Fact]
    public async Task Subscribers_CoverTechnicalUserAndMultipleLifecycleStates()
    {
        var subscribers = await _sut.GetSubscribersAsync(TestContext.Current.CancellationToken);

        subscribers.Should().Contain(s => s.IsTechnicalSubscriber);
        subscribers.Select(s => s.State).Should()
            .Contain(SubscriberState.Ready)
            .And.Contain(SubscriberState.Suspended);
    }

    [Fact]
    public async Task GetBanksAsync_IsStableAcrossCalls()
    {
        var first = await _sut.GetBanksAsync(TestContext.Current.CancellationToken);
        var second = await _sut.GetBanksAsync(TestContext.Current.CancellationToken);

        second.Should().BeSameAs(first);
    }

    // --- Keys (issue #55) ---

    [Fact]
    public async Task GetKeysAsync_ReturnsSeededSubscriberAndBankKeys()
    {
        var keys = await _sut.GetKeysAsync(TestContext.Current.CancellationToken);

        keys.Should().HaveCount(5);
        keys.Should().Contain(k => k.OwnerLabel.Contains("Teilnehmer"))
            .And.Contain(k => k.OwnerLabel.Contains("Bank"));
        keys.Select(k => k.KeyVersion).Should()
            .Contain("A006").And.Contain("E002").And.Contain("X002");
    }

    [Fact]
    public async Task GetKeysAsync_FingerprintTextMatchesCoreComputation()
    {
        var keys = await _sut.GetKeysAsync(TestContext.Current.CancellationToken);

        foreach (var key in keys)
        {
            var expected = PublicKeyFingerprint.ToLetterFormat(PublicKeyFingerprint.Compute(key.PublicKey));
            key.FingerprintText.Should().Be(expected);
        }
    }

    [Fact]
    public async Task GetKeysAsync_IsStableAcrossCalls()
    {
        var first = await _sut.GetKeysAsync(TestContext.Current.CancellationToken);
        var second = await _sut.GetKeysAsync(TestContext.Current.CancellationToken);

        second.Should().BeSameAs(first);
    }
}
