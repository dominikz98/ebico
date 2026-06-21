using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;

namespace EBICO.Tests.Infrastructure;

public class TestCertificatesTests
{
    [Fact]
    public void CreateSelfSigned_ProducesUsableCertificate()
    {
        using var cert = TestCertificates.CreateSelfSigned("CN=EBICO Unit Test");

        cert.Subject.Should().Be("CN=EBICO Unit Test");
        cert.HasPrivateKey.Should().BeTrue();
        cert.NotBefore.Should().BeBefore(DateTime.Now);
        cert.NotAfter.Should().BeAfter(DateTime.Now);

        using var rsa = cert.GetRSAPrivateKey();
        rsa.Should().NotBeNull();
        rsa!.KeySize.Should().Be(TestCertificates.DefaultKeySizeBits);
    }

    [Fact]
    public void CreateSelfSigned_SignatureVerifiesWithOwnPublicKey()
    {
        using var cert = TestCertificates.CreateSelfSigned();
        var data = "ebico"u8.ToArray();

        using var privateKey = cert.GetRSAPrivateKey();
        using var publicKey = cert.GetRSAPublicKey();
        var signature = privateKey!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        publicKey!.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .Should().BeTrue();
    }

    [Fact]
    public void CreateRsaKey_HasRequestedSize()
    {
        using var rsa = TestCertificates.CreateRsaKey(3072);

        rsa.KeySize.Should().Be(3072);
    }
}
