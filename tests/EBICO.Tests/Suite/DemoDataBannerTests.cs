using AwesomeAssertions;
using Bunit;
using EBICO.Suite.Components.Layout;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for <see cref="DemoDataBanner"/> (issue #124): the Suite runs on its own seeded in-memory
/// state and is not connected to a separately hosted <c>EBICO.Server</c> process (ADR-0009/ADR-0015).
/// Nothing in the UI said so, which made the seeded banks, subscribers and transactions read like the
/// live state of an emulator running next to it.
/// </summary>
public class DemoDataBannerTests
{
    [Fact]
    public void Renders_TheSampleDataDisclaimer()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<DemoDataBanner>();

        cut.Markup.Should().Contain("Beispieldaten");
        cut.Markup.Should().Contain("EBICO.Server");
    }

    [Fact]
    public void Renders_AsANoteLandmark()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<DemoDataBanner>();

        // Announced to assistive technology rather than being a purely visual colour cue.
        cut.Find("div.demo-banner").GetAttribute("role").Should().Be("note");
    }
}
