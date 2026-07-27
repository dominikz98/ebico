using AwesomeAssertions;
using Bunit;
using EBICO.Core.Domain;
using EBICO.Suite.Components.Stammdaten;

namespace EBICO.Tests.Suite;

/// <summary>
/// Regression tests for issue #126: the Stammdaten page hosts BankManager/PartnerManager/
/// SubscriberManager as three <em>separate</em> interactive islands. A mutation in one has to reach the
/// other two — otherwise a new bank is missing from their dropdowns and cascade-deleted rows linger
/// until a full page reload. Rendering several components in one <see cref="BunitContext"/> reproduces
/// that setup: they share the DI container, hence the same store and the same change notifier.
/// </summary>
public class StammdatenIslandSyncTests
{
    private readonly CancellationToken _ct = Xunit.TestContext.Current.CancellationToken;

    private static readonly HostId Host = HostId.Create("EBICOHOST");
    private static readonly PartnerId Partner = PartnerId.Create("CUST01");
    private static readonly UserId User = UserId.Create("USER01");

    private static IReadOnlyList<string> HostOptions(IRenderedComponent<PartnerManager> cut)
        => [.. cut.FindAll("#partner-host option").Select(o => o.GetAttribute("value")!)];

    private static IReadOnlyList<string> HostOptions(IRenderedComponent<SubscriberManager> cut)
        => [.. cut.FindAll("#sub-host option").Select(o => o.GetAttribute("value")!)];

    [Fact]
    public void NewBank_AppearsInPartnerAndSubscriberDropdowns()
    {
        using var ctx = new BunitContext();
        MasterDataTestServices.Configure(ctx);
        var banks = ctx.Render<BankManager>();
        var partners = ctx.Render<PartnerManager>();
        var subscribers = ctx.Render<SubscriberManager>();

        banks.Find("#bank-new").Click();
        banks.Find("#bank-hostid").Change("FRISCH");
        banks.Find("#bank-save").Click();

        // Open the create forms only now — before the fix they were populated from a stale bank list.
        partners.Find("#partner-new").Click();
        subscribers.Find("#subscriber-new").Click();

        HostOptions(partners).Should().Contain("FRISCH");
        HostOptions(subscribers).Should().Contain("FRISCH");
    }

    [Fact]
    public void NewBank_AppearsInAnAlreadyOpenPartnerForm()
    {
        using var ctx = new BunitContext();
        MasterDataTestServices.Configure(ctx);
        var banks = ctx.Render<BankManager>();
        var partners = ctx.Render<PartnerManager>();

        banks.Find("#bank-new").Click();
        banks.Find("#bank-hostid").Change("ERSTE");
        banks.Find("#bank-save").Click();
        partners.Find("#partner-new").Click();

        // Second bank created while the partner form is already open.
        banks.Find("#bank-new").Click();
        banks.Find("#bank-hostid").Change("ZWEITE");
        banks.Find("#bank-save").Click();

        HostOptions(partners).Should().Contain("ZWEITE");
    }

    [Fact]
    public async Task DeletedBank_DisappearsFromThePartnerDropdown()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SaveBankAsync(new Bank(HostId.Create("WEGBANK")), _ct);
        var banks = ctx.Render<BankManager>();
        var partners = ctx.Render<PartnerManager>();

        partners.Find("#partner-new").Click();
        HostOptions(partners).Should().Contain("WEGBANK");

        banks.FindAll("tr")
            .First(r => r.TextContent.Contains("WEGBANK", StringComparison.Ordinal))
            .QuerySelectorAll("button")
            .First(b => b.TextContent.Trim() == "Löschen")
            .Click();
        banks.Find("#bank-delete-confirm").Click();

        HostOptions(partners).Should().NotContain("WEGBANK");
    }

    [Fact]
    public async Task DeletingABank_DropsTheCascadedPartnerAndSubscriberRows()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SavePartnerAsync(new Partner(Host, Partner), _ct);
        await manager.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        var banks = ctx.Render<BankManager>();
        var partners = ctx.Render<PartnerManager>();
        var subscribers = ctx.Render<SubscriberManager>();

        partners.Markup.Should().Contain("CUST01");
        subscribers.Markup.Should().Contain("USER01");

        banks.FindAll("button").First(b => b.TextContent.Trim() == "Löschen").Click();
        banks.Find("#bank-delete-confirm").Click();

        // The cascade removed both server-side; the sibling islands must reflect it without a reload.
        partners.Markup.Should().NotContain("CUST01");
        partners.Markup.Should().Contain("Keine Partner registriert.");
        subscribers.Markup.Should().NotContain("USER01");
        subscribers.Markup.Should().Contain("Keine Teilnehmer registriert.");
    }

    [Fact]
    public async Task DeletingAPartner_DropsTheCascadedSubscriberRows()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SavePartnerAsync(new Partner(Host, Partner), _ct);
        await manager.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        var partners = ctx.Render<PartnerManager>();
        var subscribers = ctx.Render<SubscriberManager>();
        subscribers.Markup.Should().Contain("USER01");

        partners.FindAll("button").First(b => b.TextContent.Trim() == "Löschen").Click();
        partners.Find("#partner-delete-confirm").Click();

        subscribers.Markup.Should().NotContain("USER01");
    }

    [Fact]
    public async Task NewPartner_AppearsInTheSubscriberFormsPartnerDropdown()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);

        var partners = ctx.Render<PartnerManager>();
        var subscribers = ctx.Render<SubscriberManager>();

        // No partner yet, so the subscriber island disables its create button.
        subscribers.Find("#subscriber-new").HasAttribute("disabled").Should().BeTrue();

        partners.Find("#partner-new").Click();
        partners.Find("#partner-id").Change("NEUKUNDE");
        partners.Find("#partner-save").Click();

        subscribers.Find("#subscriber-new").HasAttribute("disabled").Should().BeFalse();
        subscribers.Find("#subscriber-new").Click();
        subscribers.FindAll("#sub-partner option").Select(o => o.GetAttribute("value"))
            .Should().Contain("NEUKUNDE");
    }

    [Fact]
    public async Task DeletedSubscriber_ClosesAnOpenDetailPanel()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(Host), _ct);
        await manager.SavePartnerAsync(new Partner(Host, Partner), _ct);
        await manager.SaveSubscriberAsync(new Subscriber(Host, Partner, User), _ct);

        var partners = ctx.Render<PartnerManager>();
        var subscribers = ctx.Render<SubscriberManager>();

        subscribers.FindAll("button").First(b => b.TextContent.Trim() == "Details").Click();
        subscribers.FindAll("#perm-add").Should().ContainSingle("the detail panel is open");

        partners.FindAll("button").First(b => b.TextContent.Trim() == "Löschen").Click();
        partners.Find("#partner-delete-confirm").Click();

        subscribers.FindAll("#perm-add").Should().BeEmpty("the subscriber behind the panel was cascaded away");
    }

    [Fact]
    public async Task OpenPartnerForm_FallsBackWhenItsSelectedBankIsDeleted()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        await manager.SaveBankAsync(new Bank(HostId.Create("AAABANK")), _ct);
        await manager.SaveBankAsync(new Bank(HostId.Create("ZZZBANK")), _ct);

        var banks = ctx.Render<BankManager>();
        var partners = ctx.Render<PartnerManager>();

        partners.Find("#partner-new").Click();
        partners.Find("#partner-host").Change("ZZZBANK");

        banks.FindAll("tr")
            .First(r => r.TextContent.Contains("ZZZBANK", StringComparison.Ordinal))
            .QuerySelectorAll("button")
            .First(b => b.TextContent.Trim() == "Löschen")
            .Click();
        banks.Find("#bank-delete-confirm").Click();

        // The form must not keep pointing at a bank that no longer exists.
        partners.Find("#partner-host").GetAttribute("value").Should().NotBe("ZZZBANK");
        partners.Markup.Should().Contain("zwischenzeitlich gelöscht");
    }

    [Fact]
    public async Task Tables_AreSortedDeterministically()
    {
        using var ctx = new BunitContext();
        var manager = MasterDataTestServices.Configure(ctx);
        foreach (var host in new[] { "ZZZBANK", "AAABANK", "MMMBANK" })
        {
            await manager.SaveBankAsync(new Bank(HostId.Create(host)), _ct);
        }

        var cut = ctx.Render<BankManager>();

        var order = cut.FindAll("tbody tr td:first-child").Select(c => c.TextContent.Trim()).ToList();
        order.Should().Equal("AAABANK", "MMMBANK", "ZZZBANK");
    }
}
