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
        var key = FakeEmulatorStateProvider.SampleKey("Subscriber TEST", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<IniLetterComparisonTool>();
        cut.Find("#expected-fingerprint").Change(key.FingerprintText);
        cut.Find("button").Click();

        cut.Find(".alert-success").TextContent.Should().Contain("matches");
    }

    [Fact]
    public void Compare_MismatchingFingerprint_ReportsFailure()
    {
        using var ctx = new BunitContext();
        var key = FakeEmulatorStateProvider.SampleKey("Subscriber TEST", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<IniLetterComparisonTool>();
        cut.Find("#expected-fingerprint").Change("00 11 22 33 44 55 66 77");
        cut.Find("button").Click();

        cut.Find(".alert-danger").TextContent.Should().Contain("does not match");
    }

    [Fact]
    public void Compare_InvalidFingerprint_ReportsWarning()
    {
        using var ctx = new BunitContext();
        var key = FakeEmulatorStateProvider.SampleKey("Subscriber TEST", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<IniLetterComparisonTool>();
        cut.Find("#expected-fingerprint").Change("nicht-hex!");
        cut.Find("button").Click();

        cut.Find(".alert-warning").TextContent.Should().Contain("hexadecimal");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compare_EmptyFingerprint_AsksForInputInsteadOfBlamingTheHex(string expected)
    {
        // Issue #126: an empty field is "nothing typed yet", not "unreadable hex" — the tool is for
        // transcribing a fingerprint off a letter, so the guidance has to differ.
        using var ctx = new BunitContext();
        var key = FakeEmulatorStateProvider.SampleKey("Subscriber TEST", KeyPurpose.Signature, "A006");
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeEmulatorStateProvider([key]));

        var cut = ctx.Render<IniLetterComparisonTool>();
        cut.Find("#expected-fingerprint").Change(expected);
        cut.Find("button").Click();

        var warning = cut.Find(".alert-warning").TextContent;
        warning.Should().Contain("Please enter the fingerprint from the INI letter");
        warning.Should().NotContain("hexadecimal");
    }
}
