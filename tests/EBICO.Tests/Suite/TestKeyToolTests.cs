using AwesomeAssertions;
using Bunit;
using EBICO.Suite.Components.Keys;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for the test-CA / key tool (<see cref="TestKeyTool"/>, issue #55): generating a key
/// shows its fingerprint and PEM, creating a certificate yields a valid verdict, and the download
/// buttons invoke the JS download helper.
/// </summary>
public class TestKeyToolTests
{
    [Fact]
    public void GenerateKey_ShowsFingerprintAndPublicKeyPem()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<TestKeyTool>();
        cut.Find("#generate-key").Click();

        cut.Markup.Should().Contain("Erzeugter Schlüssel");
        cut.Find("#public-pem").TextContent.Should().Contain("BEGIN PUBLIC KEY");
    }

    [Fact]
    public void CreateCertificate_ShowsValidVerdictAndDetails()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<TestKeyTool>();
        cut.Find("#create-cert").Click();

        cut.Find(".alert-success").TextContent.Should().Contain("gültig");
        cut.Markup.Should().Contain("Thumbprint");
    }

    [Fact]
    public void DownloadPublicKey_InvokesJsDownloadHelper()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<TestKeyTool>();
        cut.Find("#generate-key").Click();
        cut.Find("#download-public").Click();

        ctx.JSInterop.VerifyInvoke("ebicoDownload");
    }
}
