using System.Text;
using AwesomeAssertions;
using EBICO.Connector.Onboarding.Letter;
using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Crypto;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Connector.Onboarding;

/// <summary>Tests for the INI/HIA initialization-letter renderer (issue #47).</summary>
public class InitializationLetterRendererTests
{
    private static IInitializationLetterRenderer Renderer()
    {
        var transport = new FakeTransport(_ => new EbicsHttpResponse { StatusCode = 200, Payload = ReadOnlyMemory<byte>.Empty });
        var provider = OnboardingTestHarness.BuildProvider(EbicsVersion.H005, transport);
        return provider.GetRequiredService<IInitializationLetterRenderer>();
    }

    [Fact]
    public void Render_Ini_ProducesTextWithIdentifiersAndFingerprint()
    {
        var fingerprint = PublicKeyFingerprint.ToLetterFormat(PublicKeyFingerprint.Compute(RsaKeyMaterial.Generate()));
        var model = new InitializationLetterModel
        {
            Kind = LetterKind.Ini,
            HostId = "MYHOST",
            PartnerId = "PARTNER01",
            UserId = "USER0001",
            VersionCode = "H005",
            CreatedAt = OnboardingTestHarness.Now,
            Keys = [new LetterKeyEntry(KeyPurpose.Signature, "A005", fingerprint)],
        };

        var letter = Renderer().Render(model);

        letter.Text.Should().Contain("MYHOST").And.Contain("PARTNER01").And.Contain("USER0001");
        letter.Text.Should().Contain("H005").And.Contain("A005");
        // The fingerprint lines are indented in the letter; assert the first line is rendered verbatim.
        letter.Text.Should().Contain(fingerprint.Split('\n')[0]);
    }

    [Fact]
    public void Render_ProducesValidPdf()
    {
        var fingerprint = PublicKeyFingerprint.ToLetterFormat(PublicKeyFingerprint.Compute(RsaKeyMaterial.Generate()));
        var model = new InitializationLetterModel
        {
            Kind = LetterKind.Hia,
            HostId = "MYHOST",
            PartnerId = "PARTNER01",
            UserId = "USER0001",
            VersionCode = "H005",
            CreatedAt = OnboardingTestHarness.Now,
            Keys =
            [
                new LetterKeyEntry(KeyPurpose.Authentication, "X002", fingerprint),
                new LetterKeyEntry(KeyPurpose.Encryption, "E002", fingerprint),
            ],
        };

        var letter = Renderer().Render(model);

        letter.Pdf.Should().NotBeNull();
        letter.Pdf!.Length.Should().BeGreaterThan(0);
        Encoding.ASCII.GetString(letter.Pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Render_Null_Throws()
    {
        var act = () => Renderer().Render(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
