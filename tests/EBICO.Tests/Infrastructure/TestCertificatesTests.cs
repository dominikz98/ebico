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

    [Fact]
    public void CreateCertificateAuthority_IsCaWithPrivateKey()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();

        ca.HasPrivateKey.Should().BeTrue();
        var basicConstraints = ca.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        basicConstraints.CertificateAuthority.Should().BeTrue();
    }

    [Fact]
    public void IssueCertificate_ChainsToCa()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Leaf");

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        chain.Build(leaf).Should().BeTrue();
    }

    [Fact]
    public void IssueCertificate_AttachesLeafPrivateKey()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Leaf");

        leaf.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void IssueCertificate_HonoursKeyUsage()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(
            ca, "CN=EBICO Enc", X509KeyUsageFlags.KeyEncipherment);

        var keyUsage = leaf.Extensions.OfType<X509KeyUsageExtension>().Single();
        keyUsage.KeyUsages.Should().Be(X509KeyUsageFlags.KeyEncipherment);
    }

    [Fact]
    public void IssueExpired_ProducesPastWindow()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueExpired(ca);

        leaf.NotAfter.ToUniversalTime().Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void IssueNotYetValid_ProducesFutureWindow()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueNotYetValid(ca);

        leaf.NotBefore.ToUniversalTime().Should().BeAfter(DateTime.UtcNow);
    }
}
