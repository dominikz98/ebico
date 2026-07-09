using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EBICO.Core.Crypto;

/// <summary>
/// Produces self-signed X.509 certificates for EBICS 3.0 (H005) onboarding, where the public keys
/// are exchanged as certificates (<c>X509Data</c>) rather than raw <c>RSAKeyValue</c>. A BCL-only
/// wrapper around <see cref="CertificateRequest"/> (<see href="../adr/0008-krypto-bibliothek.md">ADR-0008</see>)
/// built on the issue #18 key layer (<see cref="RsaKeyMaterial"/>).
/// </summary>
/// <remarks>
/// <para>
/// The key-usage profile asserted on the certificate comes from the shared
/// <see cref="EbicsCertificateProfile"/>, so a certificate produced here always satisfies the
/// check that <see cref="X509CertificateVerifier"/> applies for the same <see cref="KeyPurpose"/>.
/// </para>
/// <para>
/// Trust in EBICS is established through the INI-letter public-key fingerprint (issue #22), not the
/// certificate chain, so these certificates are self-signed and short-lived by nature; the caller
/// supplies the validity window (no hidden <see cref="DateTime"/> dependency).
/// </para>
/// </remarks>
public static class SelfSignedCertificateFactory
{
    /// <summary>
    /// Creates a self-signed EBICS certificate for the public/private key pair in
    /// <paramref name="keyPair"/>, asserting the key-usage flags for <paramref name="purpose"/>.
    /// </summary>
    /// <param name="keyPair">The key material; must contain a private key.</param>
    /// <param name="purpose">The EBICS key purpose (drives the KeyUsage extension).</param>
    /// <param name="subjectName">The certificate subject distinguished name (e.g. <c>CN=EBICO Subscriber</c>).</param>
    /// <param name="notBefore">The start of the validity period.</param>
    /// <param name="notAfter">The end of the validity period.</param>
    /// <returns>A self-signed <see cref="X509Certificate2"/> carrying the private key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyPair"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="subjectName"/> is <see langword="null"/> or blank.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="keyPair"/> has no private key.</exception>
    public static X509Certificate2 Create(
        RsaKeyMaterial keyPair,
        KeyPurpose purpose,
        string subjectName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        ArgumentNullException.ThrowIfNull(keyPair);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectName);
        if (!keyPair.HasPrivateKey)
        {
            throw new KeyMaterialException("Cannot create a certificate: the key material has no private key.");
        }

        using var rsa = keyPair.CreateRsa();
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(EbicsCertificateProfile.KeyUsageFor(purpose), critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        return request.CreateSelfSigned(notBefore, notAfter);
    }
}
