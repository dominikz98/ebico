using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EBICO.Tests.Infrastructure;

/// <summary>
/// Produces self-signed X.509 certificates and RSA key material for tests. All
/// material is generated in-process — there are no real or proprietary keys in
/// the repository. Used by crypto/onboarding tests (M2/M3).
/// </summary>
public static class TestCertificates
{
    /// <summary>Default RSA key size used for test material, in bits.</summary>
    public const int DefaultKeySizeBits = 2048;

    /// <summary>
    /// Creates a fresh self-signed RSA certificate (valid from five minutes ago
    /// to one year from now) including its private key.
    /// </summary>
    /// <param name="subjectName">Distinguished name, e.g. <c>CN=EBICO Test</c>.</param>
    /// <param name="keySizeBits">RSA key size in bits.</param>
    public static X509Certificate2 CreateSelfSigned(
        string subjectName = "CN=EBICO Test",
        int keySizeBits = DefaultKeySizeBits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);

        using var rsa = RSA.Create(keySizeBits);
        var request = new CertificateRequest(
            subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));
    }

    /// <summary>Creates a fresh RSA key pair for tests.</summary>
    /// <param name="keySizeBits">RSA key size in bits.</param>
    public static RSA CreateRsaKey(int keySizeBits = DefaultKeySizeBits)
        => RSA.Create(keySizeBits);
}
