using AwesomeAssertions;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="MasterDataManager"/> — the master-data management layer (issue #30):
/// CRUD, referential integrity, cascading deletes, permission and lifecycle management and
/// multi-tenant isolation. Backed by the real <see cref="InMemoryEbicsStateStore"/>.
/// </summary>
public class MasterDataManagerTests
{
    private readonly InMemoryEbicsStateStore _store = new();
    private readonly MasterDataManager _sut;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static readonly HostId Host = HostId.Create("EBICOHOST");
    private static readonly PartnerId Partner = PartnerId.Create("PARTNER01");
    private static readonly UserId User = UserId.Create("USER01");

    public MasterDataManagerTests() => _sut = new MasterDataManager(_store);

    private async Task SeedBankAndPartnerAsync()
    {
        await _sut.SaveBankAsync(new Bank(Host), _ct);
        await _sut.SavePartnerAsync(new Partner(Host, Partner), _ct);
    }

    // --- CRUD happy path -------------------------------------------------------------------

    [Fact]
    public async Task SaveBank_ThenGet_RoundTrips()
    {
        await _sut.SaveBankAsync(new Bank(Host, "EBICO"), _ct);

        (await _sut.GetBankAsync(Host, _ct))!.Name.Should().Be("EBICO");
        (await _sut.GetBanksAsync(_ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task SavePartnerAndSubscriber_WithPrerequisites_Succeeds()
    {
        await SeedBankAndPartnerAsync();

        await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        (await _sut.GetSubscriberAsync(Host, Partner, User, _ct)).Should().NotBeNull();
        (await _sut.GetSubscribersAsync(Host, Partner, _ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task SaveBank_Null_Throws()
    {
        var act = async () => await _sut.SaveBankAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // --- Referential integrity -------------------------------------------------------------

    [Fact]
    public async Task SavePartner_WithoutBank_ThrowsUnknownBank()
    {
        var act = async () => await _sut.SavePartnerAsync(new Partner(Host, Partner), _ct);

        await act.Should().ThrowAsync<UnknownBankException>();
    }

    [Fact]
    public async Task SaveSubscriber_WithoutBank_ThrowsUnknownBank()
    {
        var act = async () => await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        await act.Should().ThrowAsync<UnknownBankException>();
    }

    [Fact]
    public async Task SaveSubscriber_WithBankButNoPartner_ThrowsUnknownPartner()
    {
        await _sut.SaveBankAsync(new Bank(Host), _ct);

        var act = async () => await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        await act.Should().ThrowAsync<UnknownPartnerException>();
    }

    // --- Cascading deletes -----------------------------------------------------------------

    [Fact]
    public async Task DeleteBank_CascadesToPartnersAndSubscribers()
    {
        await SeedBankAndPartnerAsync();
        await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        (await _sut.DeleteBankAsync(Host, _ct)).Should().BeTrue();

        (await _sut.GetBankAsync(Host, _ct)).Should().BeNull();
        (await _sut.GetPartnersAsync(Host, _ct)).Should().BeEmpty();
        (await _sut.GetSubscribersAsync(Host, Partner, _ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePartner_CascadesToSubscribers_ButKeepsBank()
    {
        await SeedBankAndPartnerAsync();
        await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        (await _sut.DeletePartnerAsync(Host, Partner, _ct)).Should().BeTrue();

        (await _sut.GetPartnerAsync(Host, Partner, _ct)).Should().BeNull();
        (await _sut.GetSubscribersAsync(Host, Partner, _ct)).Should().BeEmpty();
        (await _sut.GetBankAsync(Host, _ct)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteBank_Unknown_ReturnsFalse()
    {
        (await _sut.DeleteBankAsync(Host, _ct)).Should().BeFalse();
    }

    // --- Multi-tenant isolation ------------------------------------------------------------

    [Fact]
    public async Task DeleteBank_DoesNotAffectSamePartnerIdAtAnotherBank()
    {
        var otherHost = HostId.Create("BANKB");
        await _sut.SaveBankAsync(new Bank(Host), _ct);
        await _sut.SaveBankAsync(new Bank(otherHost), _ct);
        await _sut.SavePartnerAsync(new Partner(Host, Partner, "at EBICOHOST"), _ct);
        await _sut.SavePartnerAsync(new Partner(otherHost, Partner, "at BANKB"), _ct);

        await _sut.DeleteBankAsync(Host, _ct);

        (await _sut.GetPartnerAsync(otherHost, Partner, _ct))!.Name.Should().Be("at BANKB");
    }

    // --- Permissions per OrderType/BTF -----------------------------------------------------

    [Fact]
    public async Task GrantAndRevokePermission_UpdatesStoredSubscriber()
    {
        await SeedBankAndPartnerAsync();
        await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        await _sut.GrantPermissionAsync(Host, Partner, User, new SubscriberPermission("CCT", SignatureClass.E), _ct);

        (await _sut.GetSubscriberAsync(Host, Partner, User, _ct))!.CanAuthorize("CCT").Should().BeTrue();

        await _sut.RevokePermissionsAsync(Host, Partner, User, "CCT", _ct);

        (await _sut.GetSubscriberAsync(Host, Partner, User, _ct))!.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task SetPermissions_ReplacesTheWholeSet()
    {
        await SeedBankAndPartnerAsync();
        await _sut.SaveSubscriberAsync(
            new Subscriber(Host, Partner, User, permissions: [new SubscriberPermission("OLD", SignatureClass.T)]), _ct);

        await _sut.SetPermissionsAsync(Host, Partner, User,
            [new SubscriberPermission("CCT", SignatureClass.E), new SubscriberPermission("STA", SignatureClass.T)], _ct);

        var stored = await _sut.GetSubscriberAsync(Host, Partner, User, _ct);
        stored!.Permissions.Select(p => p.OrderType).Should().BeEquivalentTo(["CCT", "STA"]);
    }

    [Fact]
    public async Task GrantPermission_UnknownSubscriber_ThrowsUnknownSubscriber()
    {
        var act = async () => await _sut.GrantPermissionAsync(Host, Partner, User, new SubscriberPermission("CCT", SignatureClass.E), _ct);

        await act.Should().ThrowAsync<UnknownSubscriberException>();
    }

    // --- Lifecycle -------------------------------------------------------------------------

    [Fact]
    public async Task TransitionSubscriber_ValidTransition_UpdatesState()
    {
        await SeedBankAndPartnerAsync();
        await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        var updated = await _sut.TransitionSubscriberAsync(Host, Partner, User, SubscriberState.Initialized, _ct);

        updated.State.Should().Be(SubscriberState.Initialized);
        (await _sut.GetSubscriberAsync(Host, Partner, User, _ct))!.State.Should().Be(SubscriberState.Initialized);
    }

    [Fact]
    public async Task TransitionSubscriber_InvalidTransition_Throws()
    {
        await SeedBankAndPartnerAsync();
        await _sut.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        var act = async () => await _sut.TransitionSubscriberAsync(Host, Partner, User, SubscriberState.Ready, _ct);

        await act.Should().ThrowAsync<InvalidSubscriberStateTransitionException>();
    }

    [Fact]
    public async Task TransitionSubscriber_Unknown_ThrowsUnknownSubscriber()
    {
        var act = async () => await _sut.TransitionSubscriberAsync(Host, Partner, User, SubscriberState.Initialized, _ct);

        await act.Should().ThrowAsync<UnknownSubscriberException>();
    }

    [Fact]
    public async Task Constructor_NullStore_Throws()
    {
        var act = () => new MasterDataManager(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
