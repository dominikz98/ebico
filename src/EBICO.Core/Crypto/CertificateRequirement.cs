namespace EBICO.Core.Crypto;

/// <summary>
/// Whether an EBICS subscriber/procedure uses X.509 certificates at all. H003/H004 are
/// <b>pure-key</b> procedures — subscribers exchange raw RSA public keys (<c>RSAKeyValue</c>)
/// and trust is established via the INI-letter public-key fingerprint (issue #22), so X.509
/// verification is <b>not applicable</b>. EBICS 3.0 / H005 is <b>certificate-based</b>.
/// </summary>
/// <remarks>
/// This models the "Verfahren ohne Zertifikate" branch of issue #23 as an explicit policy: in
/// <see cref="NotUsed"/> mode the onboarding layer simply does not invoke
/// <see cref="X509CertificateVerifier"/>, so the verifier itself keeps a single responsibility
/// (verifying certificates) and is never handed a certificate it should not have.
/// </remarks>
public enum CertificateRequirement
{
    /// <summary>No certificate is used (pure RSA keys, H003/H004); X.509 verification does not apply.</summary>
    NotUsed,

    /// <summary>A certificate is required (H005) and must pass X.509 verification.</summary>
    Required,
}

/// <summary>
/// The single source of truth for whether an EBICS protocol version requires an X.509
/// certificate. Mirrors the registry style of <see cref="KeyVersions"/> / <c>EbicsVersions</c>.
/// </summary>
public static class CertificateRequirements
{
    /// <summary>
    /// Returns the certificate requirement for an EBICS protocol version — H003/H004 →
    /// <see cref="CertificateRequirement.NotUsed"/> (pure keys), H005 →
    /// <see cref="CertificateRequirement.Required"/> (certificate-based).
    /// </summary>
    /// <param name="version">The EBICS protocol version.</param>
    /// <returns>The certificate requirement for <paramref name="version"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined value.</exception>
    /// <remarks>
    /// <b>⚠️ Spec-Vorbehalt:</b> the per-version certificate requirement reflects the common
    /// reading (H005 introduced certificate-based key management). Per the project's conventions
    /// this is to be verified against the official EBICS specs/Annexe once available and adjusted
    /// here in this one place if needed.
    /// </remarks>
    public static CertificateRequirement For(EbicsVersion version) => version switch
    {
        EbicsVersion.H003 or EbicsVersion.H004 => CertificateRequirement.NotUsed,
        EbicsVersion.H005 => CertificateRequirement.Required,
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
    };
}
