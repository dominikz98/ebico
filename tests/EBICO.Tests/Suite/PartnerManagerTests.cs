using AwesomeAssertions;
using Bunit;
using EBICO.Core.Domain;
using EBICO.Suite.Components.Stammdaten;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for <see cref="PartnerManager"/> (issue #53): creating a partner for a bank (via the
/// bank dropdown), the "no banks yet" guard, and deleting a partner.
/// </summary>
public class PartnerManagerTests
{
    private readonly CancellationToken _ct = Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_AddsPartnerForSelectedBank()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(HostId.Create("EBICOHOST")), _ct);
        var cut = ctx.Render<PartnerManager>();

        cut.Find("#partner-new").Click();
        cut.Find("#partner-id").Change("CUST01");
        cut.Find("#partner-name").Change("Muster GmbH");
        cut.Find("#partner-save").Click();

        (await manager.GetPartnerAsync(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), _ct))
            .Should().NotBeNull();
        cut.Markup.Should().Contain("CUST01").And.Contain("Muster GmbH");
    }

    [Fact]
    public void NoBanks_CreateDisabledWithHint()
    {
        using var ctx = new BunitContext();
        MasterDataTestServices.Configure(ctx);

        var cut = ctx.Render<PartnerManager>();

        cut.Find("#partner-new").HasAttribute("disabled").Should().BeTrue();
        cut.Markup.Should().Contain("Zuerst eine Bank anlegen");
    }

    [Fact]
    public async Task Delete_RemovesPartner()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(HostId.Create("EBICOHOST")), _ct);
        await manager.SavePartnerAsync(new Partner(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01")), _ct);
        var cut = ctx.Render<PartnerManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Löschen").Click();
        cut.Find("#partner-delete-confirm").Click();

        (await manager.GetPartnersAsync(HostId.Create("EBICOHOST"), _ct)).Should().BeEmpty();
    }
}
