using AwesomeAssertions;
using Bunit;
using EBICO.Suite.Components.Layout;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit component tests for the Suite navigation (issue #52): the nav renders the M7
/// sections and no longer carries the Blazor template demo links.
/// </summary>
public class NavMenuTests
{
    [Fact]
    public void NavMenu_RendersExpectedNavLinks()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NavMenu>();

        var hrefs = cut.FindAll("a.nav-link").Select(a => a.GetAttribute("href")).ToArray();
        hrefs.Should().BeEquivalentTo(["", "master-data", "transactions", "keys"]);
    }

    [Fact]
    public void NavMenu_RendersExpectedLabels()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NavMenu>();

        cut.Markup.Should()
            .Contain("Dashboard")
            .And.Contain("Master Data")
            .And.Contain("Transactions")
            .And.Contain("Keys");
    }

    [Fact]
    public void NavMenu_DoesNotRenderRemovedDemoLinks()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NavMenu>();

        cut.Markup.Should().NotContain("counter").And.NotContain("weather");
    }
}
