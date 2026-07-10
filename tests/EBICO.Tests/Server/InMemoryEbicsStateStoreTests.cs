using AwesomeAssertions;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="InMemoryEbicsStateStore"/> — the pluggable in-memory server state store
/// (issues #25/#30): round-trip, unknown lookups, add-or-replace, removal, bank-scoped queries
/// and multi-tenant isolation (same ids under different banks).
/// </summary>
public class InMemoryEbicsStateStoreTests
{
    private readonly InMemoryEbicsStateStore _store = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    // --- Banks -----------------------------------------------------------------------------

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
    public async Task RemoveBank_Existing_RemovesAndReportsTrue()
    {
        var hostId = HostId.Create("EBICOHOST");
        await _store.RegisterBankAsync(new Bank(hostId), _ct);

        (await _store.RemoveBankAsync(hostId, _ct)).Should().BeTrue();
        (await _store.GetBankAsync(hostId, _ct)).Should().BeNull();
    }

    [Fact]
    public async Task RemoveBank_Unknown_ReportsFalse()
    {
        (await _store.RemoveBankAsync(HostId.Create("NOPE"), _ct)).Should().BeFalse();
    }

    // --- Partners --------------------------------------------------------------------------

    [Fact]
    public async Task RegisterPartnerThenGetPartner_ByHostAndPartner_ReturnsSamePartner()
    {
        var partner = new Partner(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), "Kunde 01");

        await _store.RegisterPartnerAsync(partner, _ct);

        (await _store.GetPartnerAsync(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), _ct))
            .Should().BeSameAs(partner);
    }

    [Fact]
    public async Task GetPartner_SamePartnerIdWrongBank_ReturnsNull()
    {
        await _store.RegisterPartnerAsync(new Partner(HostId.Create("BANKA"), PartnerId.Create("CUST01")), _ct);

        (await _store.GetPartnerAsync(HostId.Create("BANKB"), PartnerId.Create("CUST01"), _ct)).Should().BeNull();
    }

    [Fact]
    public async Task GetPartnersForBank_FiltersByHost()
    {
        await _store.RegisterPartnerAsync(new Partner(HostId.Create("BANKA"), PartnerId.Create("CUST01")), _ct);
        await _store.RegisterPartnerAsync(new Partner(HostId.Create("BANKA"), PartnerId.Create("CUST02")), _ct);
        await _store.RegisterPartnerAsync(new Partner(HostId.Create("BANKB"), PartnerId.Create("CUST01")), _ct);

        var forA = await _store.GetPartnersForBankAsync(HostId.Create("BANKA"), _ct);

        forA.Should().HaveCount(2);
        forA.Select(p => p.PartnerId.Value).Should().BeEquivalentTo(["CUST01", "CUST02"]);
    }

    [Fact]
    public async Task SamePartnerIdUnderTwoBanks_AreStoredIndependently()
    {
        await _store.RegisterPartnerAsync(new Partner(HostId.Create("BANKA"), PartnerId.Create("CUST01"), "A"), _ct);
        await _store.RegisterPartnerAsync(new Partner(HostId.Create("BANKB"), PartnerId.Create("CUST01"), "B"), _ct);

        (await _store.GetPartnersAsync(_ct)).Should().HaveCount(2);
        (await _store.GetPartnerAsync(HostId.Create("BANKA"), PartnerId.Create("CUST01"), _ct))!.Name.Should().Be("A");
        (await _store.GetPartnerAsync(HostId.Create("BANKB"), PartnerId.Create("CUST01"), _ct))!.Name.Should().Be("B");
    }

    [Fact]
    public async Task RemovePartner_Existing_RemovesAndReportsTrue()
    {
        await _store.RegisterPartnerAsync(new Partner(HostId.Create("BANKA"), PartnerId.Create("CUST01")), _ct);

        (await _store.RemovePartnerAsync(HostId.Create("BANKA"), PartnerId.Create("CUST01"), _ct)).Should().BeTrue();
        (await _store.GetPartnerAsync(HostId.Create("BANKA"), PartnerId.Create("CUST01"), _ct)).Should().BeNull();
    }

    // --- Subscribers -----------------------------------------------------------------------

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
    public async Task GetSubscribersForBankAndForPartner_FilterCorrectly()
    {
        await _store.RegisterSubscriberAsync(new Subscriber(HostId.Create("BANKA"), PartnerId.Create("P1"), UserId.Create("U1")), _ct);
        await _store.RegisterSubscriberAsync(new Subscriber(HostId.Create("BANKA"), PartnerId.Create("P1"), UserId.Create("U2")), _ct);
        await _store.RegisterSubscriberAsync(new Subscriber(HostId.Create("BANKA"), PartnerId.Create("P2"), UserId.Create("U3")), _ct);
        await _store.RegisterSubscriberAsync(new Subscriber(HostId.Create("BANKB"), PartnerId.Create("P1"), UserId.Create("U4")), _ct);

        (await _store.GetSubscribersForBankAsync(HostId.Create("BANKA"), _ct)).Should().HaveCount(3);
        (await _store.GetSubscribersForPartnerAsync(HostId.Create("BANKA"), PartnerId.Create("P1"), _ct)).Should().HaveCount(2);
        (await _store.GetSubscribersForBankAsync(HostId.Create("BANKB"), _ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task SameUserIdUnderTwoBanks_AreStoredIndependently()
    {
        await _store.RegisterSubscriberAsync(new Subscriber(HostId.Create("BANKA"), PartnerId.Create("P1"), UserId.Create("U1")), _ct);
        await _store.RegisterSubscriberAsync(new Subscriber(HostId.Create("BANKB"), PartnerId.Create("P1"), UserId.Create("U1")), _ct);

        (await _store.GetSubscribersAsync(_ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoveSubscriber_Existing_RemovesAndReportsTrue()
    {
        await _store.RegisterSubscriberAsync(new Subscriber(HostId.Create("BANKA"), PartnerId.Create("P1"), UserId.Create("U1")), _ct);

        (await _store.RemoveSubscriberAsync(HostId.Create("BANKA"), PartnerId.Create("P1"), UserId.Create("U1"), _ct)).Should().BeTrue();
        (await _store.RemoveSubscriberAsync(HostId.Create("BANKA"), PartnerId.Create("P1"), UserId.Create("U1"), _ct)).Should().BeFalse();
    }

    // --- Null guards -----------------------------------------------------------------------

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
