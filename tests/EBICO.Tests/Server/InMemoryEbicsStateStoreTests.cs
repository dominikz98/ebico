using AwesomeAssertions;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="InMemoryEbicsStateStore"/> — the pluggable in-memory server state store of
/// the host skeleton (issue #25): round-trip, unknown lookups and add-or-replace semantics.
/// </summary>
public class InMemoryEbicsStateStoreTests
{
    private readonly InMemoryEbicsStateStore _store = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegisterBankThenGetBank_ReturnsSameBank()
    {
        var bank = new Bank(HostId.Create("EBICOHOST"), "EBICO Test Bank");

        await _store.RegisterBankAsync(bank, _ct);

        (await _store.GetBankAsync(HostId.Create("EBICOHOST"), _ct)).Should().BeSameAs(bank);
        (await _store.GetBanksAsync(_ct)).Should().ContainSingle().Which.Should().BeSameAs(bank);
    }

    [Fact]
    public async Task GetBank_Unknown_ReturnsNull()
    {
        (await _store.GetBankAsync(HostId.Create("NOPE"), _ct)).Should().BeNull();
    }

    [Fact]
    public async Task RegisterBankTwice_ReplacesEntry()
    {
        var hostId = HostId.Create("EBICOHOST");
        await _store.RegisterBankAsync(new Bank(hostId, "First"), _ct);
        await _store.RegisterBankAsync(new Bank(hostId, "Second"), _ct);

        var banks = await _store.GetBanksAsync(_ct);

        banks.Should().ContainSingle().Which.Name.Should().Be("Second");
    }

    [Fact]
    public async Task RegisterSubscriberThenGetByTriple_ReturnsSubscriber()
    {
        var subscriber = new Subscriber(
            HostId.Create("EBICOHOST"),
            PartnerId.Create("PARTNER01"),
            UserId.Create("USER01"));

        await _store.RegisterSubscriberAsync(subscriber, _ct);

        var found = await _store.GetSubscriberAsync(
            HostId.Create("EBICOHOST"),
            PartnerId.Create("PARTNER01"),
            UserId.Create("USER01"),
            _ct);

        found.Should().BeSameAs(subscriber);
    }

    [Fact]
    public async Task GetSubscriber_Unknown_ReturnsNull()
    {
        var found = await _store.GetSubscriberAsync(
            HostId.Create("EBICOHOST"),
            PartnerId.Create("PARTNER01"),
            UserId.Create("USER01"),
            _ct);

        found.Should().BeNull();
    }

    [Fact]
    public async Task RegisterPartnerThenGetPartner_ReturnsSamePartner()
    {
        var partner = new Partner(PartnerId.Create("PARTNER01"), "Kunde 01");

        await _store.RegisterPartnerAsync(partner, _ct);

        (await _store.GetPartnerAsync(PartnerId.Create("PARTNER01"), _ct)).Should().BeSameAs(partner);
    }

    [Fact]
    public async Task RegisterBank_Null_Throws()
    {
        var act = async () => await _store.RegisterBankAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterPartner_Null_Throws()
    {
        var act = async () => await _store.RegisterPartnerAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterSubscriber_Null_Throws()
    {
        var act = async () => await _store.RegisterSubscriberAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
