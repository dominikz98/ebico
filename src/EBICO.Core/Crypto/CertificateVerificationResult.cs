using System.Security.Cryptography.X509Certificates;

namespace EBICO.Core.Crypto;

/// <summary>
/// The categories of X.509 certificate-verification failure reported by
/// <see cref="X509CertificateVerifier"/>. A <see cref="FlagsAttribute"/> enum so several reasons
/// can be reported together (e.g. a certificate that is both expired and untrusted).
/// </summary>
[Flags]
public enum CertificateVerificationError
{
    /// <summary>No error — the certificate passed all requested checks.</summary>
    None = 0,

    /// <summary>The chain does not terminate in a configured/trusted anchor (<c>UntrustedRoot</c> / <c>PartialChain</c>).</summary>
    UntrustedRoot = 1 << 0,

    /// <summary>The certificate (or an issuer) is outside its validity window at the verification time.</summary>
    NotTimeValid = 1 << 1,

    /// <summary>Refinement of <see cref="NotTimeValid"/>: the leaf's <c>NotAfter</c> is before the verification time.</summary>
    Expired = 1 << 2,

    /// <summary>Refinement of <see cref="NotTimeValid"/>: the leaf's <c>NotBefore</c> is after the verification time.</summary>
    NotYetValid = 1 << 3,

    /// <summary>A certificate in the chain was reported revoked.</summary>
    Revoked = 1 << 4,

    /// <summary>Revocation status could not be determined (offline/unknown) while revocation was requested.</summary>
    RevocationStatusUnknown = 1 << 5,

    /// <summary>A signature in the chain did not validate.</summary>
    InvalidSignature = 1 << 6,

    /// <summary>Basic constraints were violated (e.g. a non-CA used as issuer, or a path-length overrun).</summary>
    InvalidBasicConstraints = 1 << 7,

    /// <summary>The <c>KeyUsage</c> extension does not satisfy the EBICS profile for the expected <see cref="KeyPurpose"/>.</summary>
    InvalidKeyUsage = 1 << 8,

    /// <summary>The certificate's public key does not match <see cref="CertificateVerificationOptions.ExpectedPublicKey"/>.</summary>
    KeyMismatch = 1 << 9,

    /// <summary>The certificate carries no RSA public key (EBICS keys are RSA).</summary>
    NotRsa = 1 << 10,

    /// <summary>Any other non-<c>NoError</c> chain status not mapped to a more specific reason above.</summary>
    Other = 1 << 31,
}

/// <summary>
/// The structured outcome of X.509 certificate verification: the overall verdict, the specific
/// failing checks, the raw aggregated chain status (for diagnostics/logging) and human-readable
/// status descriptions.
/// </summary>
/// <remarks>
/// Following the project's <c>Verify</c> convention (see <see cref="BankSignature.Verify"/>), a
/// well-formed but invalid certificate yields a result with <see cref="IsValid"/> ==
/// <see langword="false"/> rather than an exception. Only <see langword="null"/> arguments throw.
/// </remarks>
public sealed class CertificateVerificationResult
{
    private static readonly IReadOnlyList<string> NoDiagnostics = [];

    internal CertificateVerificationResult(
        CertificateVerificationError errors,
        X509ChainStatusFlags chainStatus,
        IReadOnlyList<string> diagnostics)
    {
        Errors = errors;
        ChainStatus = chainStatus;
        Diagnostics = diagnostics;
    }

    /// <summary>Whether the certificate passed every requested check (i.e. <see cref="Errors"/> is <see cref="CertificateVerificationError.None"/>).</summary>
    public bool IsValid => Errors == CertificateVerificationError.None;

    /// <summary>The union of failing checks; <see cref="CertificateVerificationError.None"/> when the certificate is valid.</summary>
    public CertificateVerificationError Errors { get; }

    /// <summary>The raw aggregated <see cref="X509ChainStatusFlags"/> from chain building, for diagnostics and logging.</summary>
    public X509ChainStatusFlags ChainStatus { get; }

    /// <summary>Human-readable status descriptions (chain status information plus EBICO-level check messages).</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Creates a successful result carrying the (informational) chain status.</summary>
    /// <param name="chainStatus">The aggregated chain status flags.</param>
    /// <returns>A valid result with no errors.</returns>
    internal static CertificateVerificationResult Success(X509ChainStatusFlags chainStatus)
        => new(CertificateVerificationError.None, chainStatus, NoDiagnostics);
}
