using AwesomeAssertions;
using Bunit;
using EBICO.Core.Crypto;
using EBICO.Suite.Components.Keys;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for the INI-letter comparison tool (<see cref="IniLetterComparisonTool"/>, issue #55):
/// a matching fingerprint reports success, a mismatching one reports failure.
/// </summary>
public class IniLetterComparisonToolTests
{
    [Fact]
    public void Compare_MatchingFingerprint_ReportsSuccess()
    {
        using var ctx = new BunitContext();
        var key = FakeEmulatorStateProvider.SampleKey("Teilnehmer TEST", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<IniLetterComparisonTool>();
        cut.Find("#expected-fingerprint").Change(key.FingerprintText);
        cut.Find("button").Click();

        cut.Find(".alert-success").TextContent.Should().Contain("stimmt überein");
    }

    [Fact]
    public void Compare_MismatchingFingerprint_ReportsFailure()
    {
        using var ctx = new BunitContext();
        var key = FakeEmulatorStateProvider.SampleKey("Teilnehmer TEST", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<IniLetterComparisonTool>();
        cut.Find("#expected-fingerprint").Change("00 11 22 33 44 55 66 77");
        cut.Find("button").Click();

        cut.Find(".alert-danger").TextContent.Should().Contain("nicht");
    }

    [Fact]
    public void Compare_InvalidFingerprint_ReportsWarning()
    {
        using var ctx = new BunitContext();
        var key = FakeEmulatorStateProvider.SampleKey("Teilnehmer TEST", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<IniLetterComparisonTool>();
        cut.Find("#expected-fingerprint").Change("nicht-hex!");
        cut.Find("button").Click();

        cut.Find(".alert-warning").TextContent.Should().Contain("Hexadezimal");
    }
}
