using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="X509CertificateVerifier"/> — EBICS (H005) certificate verification: chain /
/// trust-anchor validation against a configurable test CA, validity period, the EBICS key-usage
/// profile, and optional key binding (issue #23). Tier A — all certificates are generated in-process
/// via <see cref="TestCertificates"/>; there are no proprietary samples. Deterministic via
/// <see cref="CertificateVerificationOptions.VerificationTime"/> and <see cref="X509RevocationMode.NoCheck"/>.
/// </summary>
public class X509CertificateVerifierTests
{
    // --- Happy path -----------------------------------------------------------

    [Fact]
    public void Verify_LeafChainingToTrustedCa_IsValid()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Signature Leaf");

        var result = X509CertificateVerifier.Verify(leaf, TestCertificates.TrustStore(ca), KeyPurpose.Signature);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().Be(CertificateVerificationError.None);
    }

    [Fact]
    public void Verify_SelfSignedInTrustStore_IsValid()
    {
        using var selfSigned = TestCertificates.CreateSelfSigned("CN=EBICO Trusted Self-Signed");

        var options = new CertificateVerificationOptions { TrustAnchors = TestCertificates.TrustStore(selfSigned) };
        var result = X509CertificateVerifier.Verify(selfSigned, options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_WithIntermediateInExtraStore_IsValid()
    {
        using var root = TestCertificates.CreateCertificateAuthority("CN=EBICO Root");
        using var intermediate = TestCertificates.IssueIntermediateCa(root);
        using var leaf = TestCertificates.IssueCertificate(intermediate, "CN=EBICO Leaf under Intermediate");

        var options = new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(root),
            ExtraStore = TestCertificates.TrustStore(intermediate),
        };
        var result = X509CertificateVerifier.Verify(leaf, options);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(X509KeyUsageFlags.DigitalSignature, KeyPurpose.Signature)]
    [InlineData(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, KeyPurpose.Signature)]
    [InlineData(X509KeyUsageFlags.DigitalSignature, KeyPurpose.Authentication)]
    [InlineData(X509KeyUsageFlags.KeyEncipherment, KeyPurpose.Encryption)]
    [InlineData(X509KeyUsageFlags.DataEncipherment, KeyPurpose.Encryption)]
    public void Verify_MatchingKeyUsage_IsValid(X509KeyUsageFlags usage, KeyPurpose purpose)
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Usage Leaf", usage);

        var result = X509CertificateVerifier.Verify(leaf, TestCertificates.TrustStore(ca), purpose);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_VerificationTimeWithinWindow_IsValid()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        var future = DateTimeOffset.UtcNow.AddYears(1);
        using var leaf = TestCertificates.IssueCertificate(
            ca, "CN=EBICO Future Leaf", notBefore: future, notAfter: future.AddYears(1));

        var options = new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(ca),
            VerificationTime = future.AddMonths(6),
        };
        var result = X509CertificateVerifier.Verify(leaf, options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_ExpectedPublicKeyMatches_IsValid()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Bound Leaf");
        using var leafPublic = leaf.GetRSAPublicKey()!;

        var options = new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(ca),
            ExpectedPublicKey = RsaKeyMaterial.FromPublicKey(leafPublic),
        };
        var result = X509CertificateVerifier.Verify(leaf, options);

        result.IsValid.Should().BeTrue();
    }

    // --- Negative cases -------------------------------------------------------

    [Fact]
    public void Verify_UntrustedRoot_ReturnsUntrustedRoot()
    {
        using var trustedCa = TestCertificates.CreateCertificateAuthority("CN=EBICO Trusted CA");
        using var otherCa = TestCertificates.CreateCertificateAuthority("CN=EBICO Other CA");
        using var leaf = TestCertificates.IssueCertificate(otherCa, "CN=EBICO Leaf");

        var result = X509CertificateVerifier.Verify(leaf, TestCertificates.TrustStore(trustedCa), KeyPurpose.Signature);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.UntrustedRoot);
    }

    [Fact]
    public void Verify_ExpiredCertificate_ReturnsExpired()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueExpired(ca);

        var result = X509CertificateVerifier.Verify(leaf, new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(ca),
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.Expired);
        result.Errors.Should().HaveFlag(CertificateVerificationError.NotTimeValid);
    }

    [Fact]
    public void Verify_NotYetValidCertificate_ReturnsNotYetValid()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueNotYetValid(ca);

        var result = X509CertificateVerifier.Verify(leaf, new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(ca),
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.NotYetValid);
        result.Errors.Should().HaveFlag(CertificateVerificationError.NotTimeValid);
    }

    [Fact]
    public void Verify_WrongKeyUsageForPurpose_ReturnsInvalidKeyUsage()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Enc Leaf", X509KeyUsageFlags.KeyEncipherment);

        var result = X509CertificateVerifier.Verify(leaf, TestCertificates.TrustStore(ca), KeyPurpose.Signature);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.InvalidKeyUsage);
    }

    [Fact]
    public void Verify_MissingKeyUsageExtension_ReturnsInvalidKeyUsage()
    {
        // CreateSelfSigned adds no KeyUsage extension; trusted as its own anchor so only the
        // absent-extension branch is exercised.
        using var selfSigned = TestCertificates.CreateSelfSigned("CN=EBICO No KeyUsage");

        var result = X509CertificateVerifier.Verify(
            selfSigned, TestCertificates.TrustStore(selfSigned), KeyPurpose.Signature);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.InvalidKeyUsage);
    }

    [Fact]
    public void Verify_SelfSignedNotTrusted_ReturnsUntrustedRoot()
    {
        using var selfSigned = TestCertificates.CreateSelfSigned("CN=EBICO Untrusted Self-Signed");

        var result = X509CertificateVerifier.Verify(selfSigned, new CertificateVerificationOptions());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.UntrustedRoot);
    }

    [Fact]
    public void Verify_ExpectedPublicKeyMismatch_ReturnsKeyMismatch()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Leaf");
        using var otherRsa = TestCertificates.CreateRsaKey();

        var options = new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(ca),
            ExpectedPublicKey = RsaKeyMaterial.FromPublicKey(otherRsa),
        };
        var result = X509CertificateVerifier.Verify(leaf, options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.KeyMismatch);
    }

    [Fact]
    public void Verify_NonRsaCertificate_ReturnsNotRsa()
    {
        using var ecdsa = ECDsa.Create();
        var request = new CertificateRequest("CN=EBICO ECDSA", ecdsa, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        using var ecCert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));

        // Trust the ECDSA cert as its own anchor so the chain is clean and only NotRsa is reported.
        var result = X509CertificateVerifier.Verify(ecCert, new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(ecCert),
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.NotRsa);
    }

    [Fact]
    public void Verify_MultipleFailures_ReportsAllBits()
    {
        using var trustedCa = TestCertificates.CreateCertificateAuthority("CN=EBICO Trusted CA");
        using var otherCa = TestCertificates.CreateCertificateAuthority("CN=EBICO Other CA");
        using var leaf = TestCertificates.IssueExpired(otherCa);

        var result = X509CertificateVerifier.Verify(leaf, new CertificateVerificationOptions
        {
            TrustAnchors = TestCertificates.TrustStore(trustedCa),
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveFlag(CertificateVerificationError.Expired);
        result.Errors.Should().HaveFlag(CertificateVerificationError.UntrustedRoot);
    }

    [Fact]
    public void Verify_NoCheckRevocation_DoesNotReportRevocation()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Leaf");

        var result = X509CertificateVerifier.Verify(leaf, TestCertificates.TrustStore(ca), KeyPurpose.Signature);

        result.Errors.Should().NotHaveFlag(CertificateVerificationError.Revoked);
        result.Errors.Should().NotHaveFlag(CertificateVerificationError.RevocationStatusUnknown);
    }

    // --- Null-argument guards -------------------------------------------------

    [Fact]
    public void Verify_NullCertificate_Throws()
    {
        var act = () => X509CertificateVerifier.Verify(null!, new CertificateVerificationOptions());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Verify_NullOptions_Throws()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Leaf");

        var act = () => X509CertificateVerifier.Verify(leaf, (CertificateVerificationOptions)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Verify_NullTrustAnchors_Throws()
    {
        using var ca = TestCertificates.CreateCertificateAuthority();
        using var leaf = TestCertificates.IssueCertificate(ca, "CN=EBICO Leaf");

        var act = () => X509CertificateVerifier.Verify(leaf, null!, KeyPurpose.Signature);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- Pure-key procedures (certificate-free) -------------------------------

    [Theory]
    [InlineData(EbicsVersion.H003, CertificateRequirement.NotUsed)]
    [InlineData(EbicsVersion.H004, CertificateRequirement.NotUsed)]
    [InlineData(EbicsVersion.H005, CertificateRequirement.Required)]
    public void CertificateRequirements_For_MapsVersion(EbicsVersion version, CertificateRequirement expected)
    {
        CertificateRequirements.For(version).Should().Be(expected);
    }

    [Fact]
    public void CertificateRequirements_For_UnknownVersion_Throws()
    {
        var act = () => CertificateRequirements.For((EbicsVersion)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
