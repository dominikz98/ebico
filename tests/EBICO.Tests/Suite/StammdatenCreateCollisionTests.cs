using AwesomeAssertions;
using Bunit;
using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Suite.Components.Stammdaten;

namespace EBICO.Tests.Suite;

/// <summary>
/// Regression tests for issue #126: the master-data <c>Save*</c> operations are idempotent upserts
/// (docs/server/master-data.md), so submitting the <em>create</em> form for an identity that already
/// exists used to overwrite it — resetting a subscriber to <see cref="SubscriberState.New"/> and dropping
/// its permissions, reported with a green success message. The create path must detect the collision
/// instead; the edit path must stay unaffected.
/// </summary>
public class StammdatenCreateCollisionTests
{
    private readonly CancellationToken _ct = Xunit.TestContext.Current.CancellationToken;

    private static readonly HostId Host = HostId.Create("EBICOHOST");
    private static readonly PartnerId Partner = PartnerId.Create("CUST01");
    private static readonly UserId User = UserId.Create("USER01");

    [Fact]
    public async Task Bank_Create_OnExistingHostId_IsRejectedAndKeepsTheStoredBank()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host, "Bestandsname", [EbicsVersion.H004]), _ct);
        var cut = ctx.Render<BankManager>();

        cut.Find("#bank-new").Click();
        cut.Find("#bank-hostid").Change("EBICOHOST");
        cut.Find("#bank-name").Change("Overwritten");
        cut.Find("#bank-save").Click();

        cut.Find(".alert-warning").TextContent.Should().Contain("already exists");
        var stored = await manager.GetBankAsync(Host, _ct);
        stored!.Name.Should().Be("Bestandsname");
        stored.SupportedVersions.Should().Equal(EbicsVersion.H004);
    }

    [Fact]
    public async Task Bank_Edit_OnExistingHostId_StillSaves()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host, "Alt"), _ct);
        var cut = ctx.Render<BankManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Edit").Click();
        cut.Find("#bank-name").Change("Neu");
        cut.Find("#bank-save").Click();

        (await manager.GetBankAsync(Host, _ct))!.Name.Should().Be("Neu");
    }

    [Fact]
    public async Task Partner_Create_OnExistingPartnerId_IsRejectedAndKeepsTheStoredPartner()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SavePartnerAsync(new Partner(Host, Partner, "Bestandskunde"), _ct);
        var cut = ctx.Render<PartnerManager>();

        cut.Find("#partner-new").Click();
        cut.Find("#partner-id").Change("CUST01");
        cut.Find("#partner-name").Change("Overwritten");
        cut.Find("#partner-save").Click();

        cut.Find(".alert-warning").TextContent.Should().Contain("already exists at EBICOHOST");
        (await manager.GetPartnerAsync(Host, Partner, _ct))!.Name.Should().Be("Bestandskunde");
    }

    [Fact]
    public async Task Partner_SamePartnerIdAtAnotherBank_IsAllowed()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SaveBankAsync(new Bank(HostId.Create("ZWEITBANK")), _ct);
        await manager.SavePartnerAsync(new Partner(Host, Partner), _ct);
        var cut = ctx.Render<PartnerManager>();

        cut.Find("#partner-new").Click();
        cut.Find("#partner-host").Change("ZWEITBANK");
        cut.Find("#partner-id").Change("CUST01");
        cut.Find("#partner-save").Click();

        // Multi-tenancy: the identity is (HostID, PartnerID), so this is a different customer.
        (await manager.GetPartnerAsync(HostId.Create("ZWEITBANK"), Partner, _ct)).Should().NotBeNull();
        cut.FindAll(".alert-warning").Should().BeEmpty();
    }

    [Fact]
    public async Task Subscriber_Create_OnExistingUserId_KeepsStateAndPermissions()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SavePartnerAsync(new Partner(Host, Partner), _ct);
        await manager.SaveSubscriberAsync(
            new Subscriber(Host, Partner, User, permissions: [new SubscriberPermission("CCT", SignatureClass.E)]),
            _ct);
        await manager.TransitionSubscriberAsync(Host, Partner, User, SubscriberState.Initialized, _ct);
        await manager.TransitionSubscriberAsync(Host, Partner, User, SubscriberState.Ready, _ct);

        var cut = ctx.Render<SubscriberManager>();
        cut.Find("#subscriber-new").Click();
        cut.Find("#sub-user").Change("USER01");
        cut.Find("#subscriber-save").Click();

        cut.Find(".alert-warning").TextContent.Should().Contain("already exists at EBICOHOST/CUST01");

        var stored = await manager.GetSubscriberAsync(Host, Partner, User, _ct);
        stored!.State.Should().Be(SubscriberState.Ready, "the create form must not reset the lifecycle");
        stored.Permissions.Should().ContainSingle(p => p.OrderType == "CCT" && p.SignatureClass == SignatureClass.E);
    }

    [Fact]
    public async Task Subscriber_SameUserIdAtAnotherPartner_IsAllowed()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SavePartnerAsync(new Partner(Host, Partner), _ct);
        await manager.SavePartnerAsync(new Partner(Host, PartnerId.Create("CUST02")), _ct);
        await manager.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        var cut = ctx.Render<SubscriberManager>();
        cut.Find("#subscriber-new").Click();
        cut.Find("#sub-partner").Change("CUST02");
        cut.Find("#sub-user").Change("USER01");
        cut.Find("#subscriber-save").Click();

        (await manager.GetSubscriberAsync(Host, PartnerId.Create("CUST02"), User, _ct)).Should().NotBeNull();
        cut.FindAll(".alert-warning").Should().BeEmpty();
    }
}
