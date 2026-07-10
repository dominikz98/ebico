using AwesomeAssertions;
using Bunit;
using EBICO.Core.Domain;
using EBICO.Server.State;
using EBICO.Suite.Components.Stammdaten;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for <see cref="SubscriberManager"/> (issue #53): creating a subscriber via the
/// dependent bank/partner dropdowns, lifecycle transitions, editing permissions and deletion.
/// </summary>
public class SubscriberManagerTests
{
    private readonly CancellationToken _ct = Xunit.TestContext.Current.CancellationToken;

    private async Task SeedBankAndPartnerAsync(IMasterDataManager manager)
    {
        await manager.SaveBankAsync(new Bank(HostId.Create("EBICOHOST")), _ct);
        await manager.SavePartnerAsync(new Partner(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01")), _ct);
    }

    private async Task SeedSubscriberAsync(IMasterDataManager manager)
    {
        await SeedBankAndPartnerAsync(manager);
        await manager.SaveSubscriberAsync(
            new Subscriber(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), UserId.Create("USER01")), _ct);
    }

    [Fact]
    public async Task Create_AddsSubscriberViaDropdowns()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await SeedBankAndPartnerAsync(manager);
        var cut = ctx.Render<SubscriberManager>();

        cut.Find("#subscriber-new").Click();
        cut.Find("#sub-user").Change("USER01");
        cut.Find("#subscriber-save").Click();

        var created = await manager.GetSubscriberAsync(
            HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), UserId.Create("USER01"), _ct);
        created.Should().NotBeNull();
        created!.State.Should().Be(SubscriberState.New);
    }

    [Fact]
    public async Task StatusAction_MovesSubscriberToInitialized()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await SeedSubscriberAsync(manager);
        var cut = ctx.Render<SubscriberManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Details").Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Initialisieren").Click();

        var updated = await manager.GetSubscriberAsync(
            HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), UserId.Create("USER01"), _ct);
        updated!.State.Should().Be(SubscriberState.Initialized);
    }

    [Fact]
    public async Task Permissions_AddAndSave()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await SeedSubscriberAsync(manager);
        var cut = ctx.Render<SubscriberManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Details").Click();
        cut.Find("#perm-add").Click();
        cut.Find("input.form-control-sm").Change("CCT");
        cut.Find("#perm-save").Click();

        var updated = await manager.GetSubscriberAsync(
            HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), UserId.Create("USER01"), _ct);
        updated!.Permissions.Should().ContainSingle(p => p.OrderType == "CCT" && p.SignatureClass == SignatureClass.T);
    }

    [Fact]
    public async Task Delete_RemovesSubscriber()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await SeedSubscriberAsync(manager);
        var cut = ctx.Render<SubscriberManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Löschen").Click();
        cut.Find("#subscriber-delete-confirm").Click();

        (await manager.GetSubscriberAsync(
            HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), UserId.Create("USER01"), _ct))
            .Should().BeNull();
    }
}
