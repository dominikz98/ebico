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

    /// <summary>
    /// Creates a self-signed test root certificate authority (BasicConstraints cA=true critical,
    /// KeyUsage KeyCertSign|CrlSign critical, SubjectKeyIdentifier), valid for ten years, retaining
    /// its private key so it can issue leaf certificates via <see cref="IssueCertificate"/>.
    /// </summary>
    /// <param name="subjectName">Distinguished name of the CA.</param>
    /// <param name="keySizeBits">RSA key size in bits.</param>
    public static X509Certificate2 CreateCertificateAuthority(
        string subjectName = "CN=EBICO Test Root CA",
        int keySizeBits = DefaultKeySizeBits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);

        using var rsa = RSA.Create(keySizeBits);
        var request = new CertificateRequest(
            subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Wide window (-10y..+10y) so expired / not-yet-valid leaf fixtures nest cleanly within the
        // CA validity period at their respective verification times.
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddYears(-10), now.AddYears(10));
    }

    /// <summary>
    /// Issues a leaf certificate signed by <paramref name="issuer"/> (which must hold a private key).
    /// Adds BasicConstraints cA=false, the given <paramref name="keyUsage"/>, a SubjectKeyIdentifier
    /// and an AuthorityKeyIdentifier. The returned certificate includes the freshly generated leaf
    /// private key.
    /// </summary>
    /// <param name="issuer">The signing CA certificate (must have a private key).</param>
    /// <param name="subjectName">Distinguished name of the leaf.</param>
    /// <param name="keyUsage">The KeyUsage flags to assert on the leaf.</param>
    /// <param name="notBefore">Validity start; defaults to five minutes ago.</param>
    /// <param name="notAfter">Validity end; defaults to one year from now.</param>
    /// <param name="keySizeBits">RSA key size in bits.</param>
    public static X509Certificate2 IssueCertificate(
        X509Certificate2 issuer,
        string subjectName,
        X509KeyUsageFlags keyUsage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        int keySizeBits = DefaultKeySizeBits)
        => Issue(issuer, subjectName, isCertificateAuthority: false, keyUsage, notBefore, notAfter, keySizeBits);

    /// <summary>
    /// Issues an intermediate CA certificate (BasicConstraints cA=true, KeyUsage KeyCertSign|CrlSign)
    /// signed by <paramref name="issuer"/>, retaining its private key so it can in turn issue leaves.
    /// </summary>
    /// <param name="issuer">The signing (root) CA certificate (must have a private key).</param>
    /// <param name="subjectName">Distinguished name of the intermediate CA.</param>
    /// <param name="keySizeBits">RSA key size in bits.</param>
    public static X509Certificate2 IssueIntermediateCa(
        X509Certificate2 issuer,
        string subjectName = "CN=EBICO Test Intermediate CA",
        int keySizeBits = DefaultKeySizeBits)
    {
        // Wide window (-5y..+5y): strictly inside the root CA (-10y..+10y) yet comfortably wider than
        // the leaf window (-5min..+1y), so a leaf issued a moment later never exceeds the intermediate's
        // NotAfter (which the default -5min..+1y window did, straddling a one-second boundary — flaky).
        var now = DateTimeOffset.UtcNow;
        return Issue(issuer, subjectName, isCertificateAuthority: true,
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            notBefore: now.AddYears(-5), notAfter: now.AddYears(5), keySizeBits);
    }

    /// <summary>Issues an already-expired leaf (valid two years ago to one year ago).</summary>
    /// <param name="issuer">The signing CA certificate.</param>
    /// <param name="subjectName">Distinguished name of the leaf.</param>
    public static X509Certificate2 IssueExpired(X509Certificate2 issuer, string subjectName = "CN=EBICO Expired Leaf")
    {
        var now = DateTimeOffset.UtcNow;
        return IssueCertificate(issuer, subjectName, notBefore: now.AddYears(-2), notAfter: now.AddYears(-1));
    }

    /// <summary>Issues a not-yet-valid leaf (validity starts one year from now).</summary>
    /// <param name="issuer">The signing CA certificate.</param>
    /// <param name="subjectName">Distinguished name of the leaf.</param>
    public static X509Certificate2 IssueNotYetValid(X509Certificate2 issuer, string subjectName = "CN=EBICO Future Leaf")
    {
        var now = DateTimeOffset.UtcNow;
        return IssueCertificate(issuer, subjectName, notBefore: now.AddYears(1), notAfter: now.AddYears(2));
    }

    /// <summary>Builds a collection of trust anchors, for <c>CertificateVerificationOptions.TrustAnchors</c>.</summary>
    /// <param name="anchors">The certificates to trust as roots.</param>
    public static X509Certificate2Collection TrustStore(params X509Certificate2[] anchors)
    {
        var store = new X509Certificate2Collection();
        store.AddRange(anchors);
        return store;
    }

    private static X509Certificate2 Issue(
        X509Certificate2 issuer,
        string subjectName,
        bool isCertificateAuthority,
        X509KeyUsageFlags keyUsage,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        int keySizeBits)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);

        using var rsa = RSA.Create(keySizeBits);
        var request = new CertificateRequest(
            subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(isCertificateAuthority, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        var now = DateTimeOffset.UtcNow;
        var start = notBefore ?? now.AddMinutes(-5);
        var end = notAfter ?? now.AddYears(1);

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F; // positive DER integer

        using var issued = request.Create(issuer, start, end, serial);
        return issued.CopyWithPrivateKey(rsa);
    }
}
