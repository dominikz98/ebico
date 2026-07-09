using System.Security.Cryptography.X509Certificates;

namespace EBICO.Core.Crypto;

/// <summary>
/// The single source of truth for the EBICS X.509 <i>key-usage</i> profile per
/// <see cref="KeyPurpose"/>. Both the certificate <b>producer</b>
/// (<see cref="SelfSignedCertificateFactory"/>) and the certificate <b>verifier</b>
/// (<see cref="X509CertificateVerifier"/>) consume this profile, so a generated EBICS (H005)
/// certificate always satisfies the check the verifier applies — the two cannot drift apart.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the EBICS X.509 key-usage profile per purpose is not yet verified
/// against the official EBICS Annex (the XSDs/specs are proprietary and not in the repo — see
/// <c>CLAUDE.md</c>). This is the one place to adjust it. Signature and authentication certificates
/// are required to assert <c>DigitalSignature</c>; <c>NonRepudiation</c> is permitted but not
/// required (tighten by moving it into <c>AllOf</c> in <see cref="RequiredKeyUsage"/>). Encryption
/// certificates must assert <c>KeyEncipherment</c> or <c>DataEncipherment</c>. Extended Key Usage is
/// deliberately not part of the profile (EBICS defines no standard EKU OIDs for A/E/X keys).
/// </remarks>
public static class EbicsCertificateProfile
{
    /// <summary>
    /// The key-usage requirement a certificate of the given <paramref name="purpose"/> must satisfy:
    /// every bit in <c>AllOf</c> must be present, and — when <c>AnyOf</c> is not
    /// <see cref="X509KeyUsageFlags.None"/> — at least one <c>AnyOf</c> bit must be present.
    /// </summary>
    /// <param name="purpose">The EBICS key purpose.</param>
    /// <returns>The <c>AllOf</c>/<c>AnyOf</c> key-usage requirement.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not a defined value.</exception>
    public static (X509KeyUsageFlags AllOf, X509KeyUsageFlags AnyOf) RequiredKeyUsage(KeyPurpose purpose) => purpose switch
    {
        KeyPurpose.Signature => (X509KeyUsageFlags.DigitalSignature, X509KeyUsageFlags.None),
        KeyPurpose.Authentication => (X509KeyUsageFlags.DigitalSignature, X509KeyUsageFlags.None),
        KeyPurpose.Encryption => (X509KeyUsageFlags.None, X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment),
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown key purpose."),
    };

    /// <summary>
    /// The concrete key-usage flags to assert when <b>creating</b> a self-signed EBICS certificate
    /// for the given <paramref name="purpose"/>. The returned set is guaranteed to satisfy
    /// <see cref="RequiredKeyUsage"/> for the same purpose.
    /// </summary>
    /// <param name="purpose">The EBICS key purpose.</param>
    /// <returns>The key-usage flags to place on the certificate.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not a defined value.</exception>
    public static X509KeyUsageFlags KeyUsageFor(KeyPurpose purpose) => purpose switch
    {
        KeyPurpose.Signature => X509KeyUsageFlags.DigitalSignature,
        KeyPurpose.Authentication => X509KeyUsageFlags.DigitalSignature,
        KeyPurpose.Encryption => X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown key purpose."),
    };
}
