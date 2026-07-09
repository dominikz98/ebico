using System.Security.Cryptography.X509Certificates;

namespace EBICO.Core.Crypto;

/// <summary>
/// Configuration for <see cref="X509CertificateVerifier.Verify(X509Certificate2, CertificateVerificationOptions)"/>:
/// the trust anchors, revocation policy, verification time and the EBICS profile checks to apply.
/// </summary>
/// <remarks>
/// The defaults are chosen so the offline/test path works without network access:
/// <see cref="X509ChainTrustMode.CustomRootTrust"/> plus <see cref="X509RevocationMode.NoCheck"/>.
/// A production caller flips <see cref="RevocationMode"/> / <see cref="TrustMode"/> as needed. The
/// verifier does not take ownership of the supplied certificate collections; the caller disposes them.
/// </remarks>
public sealed class CertificateVerificationOptions
{
    /// <summary>
    /// The trust anchors (root CAs, or self-signed bank certificates) trusted when
    /// <see cref="TrustMode"/> is <see cref="X509ChainTrustMode.CustomRootTrust"/>.
    /// </summary>
    public X509Certificate2Collection TrustAnchors { get; init; } = [];

    /// <summary>Additional intermediate certificates made available to chain building (not themselves trusted).</summary>
    public X509Certificate2Collection ExtraStore { get; init; } = [];

    /// <summary>
    /// How the root is trusted. Default <see cref="X509ChainTrustMode.CustomRootTrust"/>
    /// (deterministic, test-friendly); set <see cref="X509ChainTrustMode.System"/> to also honour
    /// the operating-system trust store.
    /// </summary>
    public X509ChainTrustMode TrustMode { get; init; } = X509ChainTrustMode.CustomRootTrust;

    /// <summary>
    /// Revocation checking mode. Default <see cref="X509RevocationMode.NoCheck"/> —
    /// offline/deterministic; EBICS revocation via CRL/OCSP is an online deployment concern.
    /// </summary>
    public X509RevocationMode RevocationMode { get; init; } = X509RevocationMode.NoCheck;

    /// <summary>Which chain elements revocation applies to. Default <see cref="X509RevocationFlag.ExcludeRoot"/>.</summary>
    public X509RevocationFlag RevocationFlag { get; init; } = X509RevocationFlag.ExcludeRoot;

    /// <summary>Extra chain-engine verification flags. Default <see cref="X509VerificationFlags.NoFlag"/> (strict).</summary>
    public X509VerificationFlags VerificationFlags { get; init; } = X509VerificationFlags.NoFlag;

    /// <summary>
    /// The instant the certificate is validated as of. <see langword="null"/> means "now". Enables
    /// deterministic tests of the expiry / not-yet-valid paths.
    /// </summary>
    public DateTimeOffset? VerificationTime { get; init; }

    /// <summary>
    /// The EBICS key purpose the certificate's <c>KeyUsage</c> extension must satisfy.
    /// <see langword="null"/> skips the key-usage check.
    /// </summary>
    public KeyPurpose? ExpectedPurpose { get; init; }

    /// <summary>
    /// An optional binding: the certificate's RSA public key must equal this material (canonical
    /// modulus/exponent). <see langword="null"/> skips the binding. Used to bind an H005
    /// certificate to the separately exchanged subscriber key.
    /// </summary>
    public RsaKeyMaterial? ExpectedPublicKey { get; init; }
}
