using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>Tests for the H005 self-signed certificate factory (issue #47).</summary>
public class SelfSignedCertificateFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(KeyPurpose.Signature)]
    [InlineData(KeyPurpose.Authentication)]
    [InlineData(KeyPurpose.Encryption)]
    public void Create_ProducesCertificatePassingTheVerifier(KeyPurpose purpose)
    {
        var key = RsaKeyMaterial.Generate();

        using var cert = SelfSignedCertificateFactory.Create(
            key, purpose, "CN=EBICO Test", Now.AddMinutes(-5), Now.AddYears(1));

        // Deterministic: verify "as of" the same fixed instant the certificate window brackets.
        var options = new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(cert),
            ExpectedPurpose = purpose,
            VerificationTime = Now,
        };
        var result = X509CertificateVerifier.Verify(cert, options);

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Diagnostics));
    }

    [Theory]
    [InlineData(KeyPurpose.Signature, X509KeyUsageFlags.DigitalSignature)]
    [InlineData(KeyPurpose.Authentication, X509KeyUsageFlags.DigitalSignature)]
    [InlineData(KeyPurpose.Encryption, X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment)]
    public void Create_AssertsTheExpectedKeyUsage(KeyPurpose purpose, X509KeyUsageFlags expected)
    {
        var key = RsaKeyMaterial.Generate();

        using var cert = SelfSignedCertificateFactory.Create(
            key, purpose, "CN=EBICO Test", Now.AddMinutes(-5), Now.AddYears(1));

        var keyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().Single();
        (keyUsage.KeyUsages & expected).Should().Be(expected);
    }

    [Fact]
    public void Create_BindsTheOriginalPublicKey()
    {
        var key = RsaKeyMaterial.Generate();

        using var cert = SelfSignedCertificateFactory.Create(
            key, KeyPurpose.Authentication, "CN=EBICO Test", Now.AddMinutes(-5), Now.AddYears(1));

        var fromCert = RsaKeyImportExport.ImportPublicKeyFromCertificate(cert);
        fromCert.Modulus.ToArray().Should().Equal(key.Modulus.ToArray());
        fromCert.Exponent.ToArray().Should().Equal(key.Exponent.ToArray());
    }

    [Fact]
    public void Create_PublicOnlyKey_Throws()
    {
        var publicOnly = RsaKeyMaterial.Generate().ToPublicOnly();

        var act = () => SelfSignedCertificateFactory.Create(
            publicOnly, KeyPurpose.Signature, "CN=EBICO Test", Now.AddMinutes(-5), Now.AddYears(1));

        act.Should().Throw<KeyMaterialException>();
    }
}
