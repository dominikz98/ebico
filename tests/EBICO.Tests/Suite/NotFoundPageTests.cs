using AwesomeAssertions;
using Bunit;
using EBICO.Suite.Components.Pages;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for the 404 page (<see cref="NotFound"/>, issue #126). It used to be the Blazor template
/// leftover: English prose in an otherwise German shell, no page title, and an <c>h3</c> that left
/// <c>&lt;FocusOnNavigate Selector="h1" /&gt;</c> in <c>Routes.razor</c> without a target.
/// </summary>
public class NotFoundPageTests
{
    [Fact]
    public void Renders_GermanHeading_AsH1()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NotFound>();

        cut.Find("h1").TextContent.Trim().Should().Be("Seite nicht gefunden");
    }

    [Fact]
    public void Renders_NoEnglishTemplateLeftovers()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NotFound>();

        cut.Markup.Should().NotContain("Not Found");
        cut.Markup.Should().NotContain("Sorry");
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
