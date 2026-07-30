using AwesomeAssertions;
using Bunit;
using EBICO.Suite.Components.Pages;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for the 404 page (<see cref="NotFound"/>, issue #126). It used to be the Blazor template
/// leftover: a mismatched-language shell, no page title, and an <c>h3</c> that left
/// <c>&lt;FocusOnNavigate Selector="h1" /&gt;</c> in <c>Routes.razor</c> without a target.
/// </summary>
public class NotFoundPageTests
{
    [Fact]
    public void Renders_Heading_AsH1()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NotFound>();

        cut.Find("h1").TextContent.Trim().Should().Be("Page not found");
    }

    [Fact]
    public void Renders_CustomCopy_NotTemplateLeftover()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NotFound>();

        cut.Markup.Should().Contain("does not belong to any page of this interface");
        cut.Markup.Should().NotContain("Sorry", "the Blazor template leftover must be gone");
        cut.FindAll("h3").Should().BeEmpty("the heading must be an h1 so FocusOnNavigate finds it");
    }

    [Fact]
    public void Offers_AWayBackToTheDashboard()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NotFound>();

        cut.Find("a").GetAttribute("href").Should().Be(string.Empty, "the dashboard sits at the base path");
    }
}
