using AwesomeAssertions;
using Bunit;
using EBICO.Core.Domain;
using EBICO.Suite.Components.Stammdaten;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for <see cref="BankManager"/> (issue #53): listing, creating (incl. invalid input)
/// and deleting banks via the real master-data manager.
/// </summary>
public class BankManagerTests
{
    private readonly CancellationToken _ct = Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task Renders_SeededBanks()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(HostId.Create("EBICOHOST"), "EBICO Test-Bank"), _ct);

        var cut = ctx.Render<BankManager>();

        cut.Markup.Should().Contain("EBICOHOST").And.Contain("EBICO Test-Bank");
    }

    [Fact]
    public async Task Create_AddsBank()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        var cut = ctx.Render<BankManager>();

        cut.Find("#bank-new").Click();
        cut.Find("#bank-hostid").Change("NEWHOST");
        cut.Find("#bank-name").Change("Neue Bank");
        cut.Find("#bank-save").Click();

        (await manager.GetBankAsync(HostId.Create("NEWHOST"), _ct)).Should().NotBeNull();
        cut.Markup.Should().Contain("NEWHOST");
    }

    [Fact]
    public void Create_InvalidHostId_ShowsWarning()
    {
        using var ctx = new BunitContext();
        MasterDataTestServices.Configure(ctx);
        var cut = ctx.Render<BankManager>();

        cut.Find("#bank-new").Click();
        cut.Find("#bank-hostid").Change("bad id!");
        cut.Find("#bank-save").Click();

        cut.Find(".alert-warning").TextContent.Should().Contain("Ungültige HostID");
    }

    [Fact]
    public async Task Delete_RemovesBank()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(HostId.Create("EBICOHOST")), _ct);
        var cut = ctx.Render<BankManager>();

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Löschen").Click();
        cut.Find("#bank-delete-confirm").Click();

        (await manager.GetBanksAsync(_ct)).Should().BeEmpty();
    }
}
