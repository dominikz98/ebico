using AwesomeAssertions;
using Bunit;
using EBICO.Core.Crypto;
using EBICO.Suite.Components.Pages;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for the key/certificate view page (<see cref="Schluessel"/>, issue #55): it renders
/// the known-key fingerprints from <see cref="IEmulatorStateProvider"/> and the key-version catalog.
/// </summary>
public class SchluesselPageTests
{
    [Fact]
    public void Page_RendersKnownKeyFingerprints()
    {
        using var ctx = new BunitContext();
        var key = FakeEmulatorStateProvider.SampleKey("Teilnehmer TESTKEY", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<Schluessel>();

        cut.Markup.Should().Contain("Teilnehmer TESTKEY");
        cut.Markup.Should().Contain(key.FingerprintText.Split('\n')[0]);
    }

    [Fact]
    public void Page_RendersKeyVersionCatalog()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([]));

        var cut = ctx.Render<Schluessel>();

        // The static KeyVersions catalog lists all known versions.
        cut.Markup.Should()
            .Contain("A004").And.Contain("A005").And.Contain("A006")
            .And.Contain("E001").And.Contain("E002")
            .And.Contain("X001").And.Contain("X002");
    }
}
