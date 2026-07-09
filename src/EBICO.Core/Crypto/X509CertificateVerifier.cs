using System.Security.Cryptography.X509Certificates;

namespace EBICO.Core.Crypto;

/// <summary>
/// Verifies an X.509 certificate for EBICS (H005) use: builds and validates the chain against a
/// configurable trust-anchor set, checks the validity period and the EBICS key-usage profile, and
/// optionally binds the certificate to a known subscriber public key. A BCL-only wrapper around
/// <see cref="X509Chain"/> (<see href="../adr/0008-krypto-bibliothek.md">ADR-0008</see>).
/// </summary>
/// <remarks>
/// <para>
/// Stateless. The pure-key procedures of H003/H004 (no certificates) are represented by
/// <see cref="CertificateRequirement"/> and simply do not invoke this verifier — trust is
/// established there via the INI-letter public-key fingerprint (issue #22).
/// </para>
/// <para>
/// Following the project's <c>Verify</c> convention (<see cref="BankSignature.Verify"/>,
/// <see cref="PublicKeyFingerprint.Verify"/>), a certificate that is well-formed but fails a check
/// yields a <see cref="CertificateVerificationResult"/> with <see cref="CertificateVerificationResult.IsValid"/>
/// == <see langword="false"/> and the failing reasons — it does not throw. Only <see langword="null"/>
/// arguments throw. The overall verdict is derived from the <b>mapped</b> reasons, not from
/// <see cref="X509Chain.Build(X509Certificate2)"/>'s boolean, so (for example) with
/// <see cref="X509RevocationMode.NoCheck"/> a missing revocation response never fails verification.
/// </para>
/// </remarks>
public static class X509CertificateVerifier
{
    // Chain-status flags this verifier maps to a specific reason. Anything set outside this mask
    // (and not NoError) is reported as CertificateVerificationError.Other.
    private const X509ChainStatusFlags HandledChainStatus =
        X509ChainStatusFlags.UntrustedRoot
        | X509ChainStatusFlags.PartialChain
        | X509ChainStatusFlags.NotTimeValid
        | X509ChainStatusFlags.Revoked
        | X509ChainStatusFlags.RevocationStatusUnknown
        | X509ChainStatusFlags.OfflineRevocation
        | X509ChainStatusFlags.NotSignatureValid
        | X509ChainStatusFlags.InvalidBasicConstraints
        | X509ChainStatusFlags.NotValidForUsage
        | X509ChainStatusFlags.HasNotSupportedCriticalExtension;

    /// <summary>
    /// Verifies <paramref name="certificate"/> against <paramref name="options"/>.
    /// </summary>
    /// <param name="certificate">The end-entity (leaf) certificate to verify.</param>
    /// <param name="options">Trust anchors, revocation policy, verification time and the EBICS checks to apply.</param>
    /// <returns>A structured result carrying the overall verdict and the per-check reasons.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="certificate"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static CertificateVerificationResult Verify(
        X509Certificate2 certificate, CertificateVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(options);

        var errors = CertificateVerificationError.None;
        var diagnostics = new List<string>();

        var chainStatus = BuildAndMapChain(certificate, options, ref errors, diagnostics);
        RefineValidity(certificate, options, ref errors);
        CheckKeyUsage(certificate, options, ref errors, diagnostics);
        CheckPublicKey(certificate, options, ref errors, diagnostics);

        return errors == CertificateVerificationError.None
            ? CertificateVerificationResult.Success(chainStatus)
            : new CertificateVerificationResult(errors, chainStatus, diagnostics);
    }

    /// <summary>
    /// Convenience overload: verifies <paramref name="certificate"/> against the given
    /// <paramref name="trustAnchors"/> for a single expected <paramref name="expectedPurpose"/>,
    /// using the default revocation/time policy otherwise.
    /// </summary>
    /// <param name="certificate">The end-entity (leaf) certificate to verify.</param>
    /// <param name="trustAnchors">The trusted roots / self-signed anchors.</param>
    /// <param name="expectedPurpose">The EBICS key purpose whose key-usage profile must be satisfied.</param>
    /// <returns>A structured result carrying the overall verdict and the per-check reasons.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="certificate"/> or <paramref name="trustAnchors"/> is <see langword="null"/>.</exception>
    public static CertificateVerificationResult Verify(
        X509Certificate2 certificate, X509Certificate2Collection trustAnchors, KeyPurpose expectedPurpose)
    {
        ArgumentNullException.ThrowIfNull(trustAnchors);
        return Verify(certificate, new CertificateVerificationOptions
        {
            TrustAnchors = trustAnchors,
            ExpectedPurpose = expectedPurpose,
        });
    }

    private static X509ChainStatusFlags BuildAndMapChain(
        X509Certificate2 certificate,
        CertificateVerificationOptions options,
        ref CertificateVerificationError errors,
        List<string> diagnostics)
    {
        using var chain = new X509Chain();
        var policy = chain.ChainPolicy;

        policy.TrustMode = options.TrustMode;
        if (options.TrustMode == X509ChainTrustMode.CustomRootTrust)
        {
            policy.CustomTrustStore.AddRange(options.TrustAnchors);
        }

        policy.ExtraStore.AddRange(options.ExtraStore);
        policy.RevocationMode = options.RevocationMode;
        policy.RevocationFlag = options.RevocationFlag;
        policy.VerificationFlags = options.VerificationFlags;

        // Keep verification hermetic and deterministic: no AIA/CRL/OCSP network fetches.
        policy.DisableCertificateDownloads = true;

        if (options.VerificationTime is { } verificationTime)
        {
            // ChainPolicy.VerificationTime is a DateTime; an Unspecified Kind is treated as local
            // time by the platform, so pin it to UTC.
            policy.VerificationTime = verificationTime.UtcDateTime;
        }

        // Copy out only value types / strings before the chain (and its element certificates) are
        // disposed at the end of the using block.
        _ = chain.Build(certificate);

        var aggregate = X509ChainStatusFlags.NoError;
        foreach (var status in chain.ChainStatus)
        {
            aggregate |= status.Status;
            var info = status.StatusInformation?.Trim();
            if (!string.IsNullOrEmpty(info))
            {
                diagnostics.Add(info);
            }
        }

        MapChainStatus(aggregate, ref errors);
        return aggregate;
    }

    private static void MapChainStatus(X509ChainStatusFlags aggregate, ref CertificateVerificationError errors)
    {
        if ((aggregate & (X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain)) != 0)
        {
            errors |= CertificateVerificationError.UntrustedRoot;
        }

        if ((aggregate & X509ChainStatusFlags.NotTimeValid) != 0)
        {
            errors |= CertificateVerificationError.NotTimeValid;
        }

        if ((aggregate & X509ChainStatusFlags.Revoked) != 0)
        {
            errors |= CertificateVerificationError.Revoked;
        }

        if ((aggregate & (X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation)) != 0)
        {
            errors |= CertificateVerificationError.RevocationStatusUnknown;
        }

        if ((aggregate & X509ChainStatusFlags.NotSignatureValid) != 0)
        {
            errors |= CertificateVerificationError.InvalidSignature;
        }

        if ((aggregate & X509ChainStatusFlags.InvalidBasicConstraints) != 0)
        {
            errors |= CertificateVerificationError.InvalidBasicConstraints;
        }

        if ((aggregate & (X509ChainStatusFlags.NotValidForUsage | X509ChainStatusFlags.HasNotSupportedCriticalExtension)) != 0)
        {
            errors |= CertificateVerificationError.InvalidKeyUsage;
        }

        if ((aggregate & ~HandledChainStatus) != 0)
        {
            errors |= CertificateVerificationError.Other;
        }
    }

    private static void RefineValidity(
        X509Certificate2 certificate, CertificateVerificationOptions options, ref CertificateVerificationError errors)
    {
        var instant = options.VerificationTime?.UtcDateTime ?? DateTime.UtcNow;

        // X509Certificate2.NotBefore / NotAfter are returned as local time; compare in UTC.
        if (instant > certificate.NotAfter.ToUniversalTime())
        {
            errors |= CertificateVerificationError.Expired | CertificateVerificationError.NotTimeValid;
        }

        if (instant < certificate.NotBefore.ToUniversalTime())
        {
            errors |= CertificateVerificationError.NotYetValid | CertificateVerificationError.NotTimeValid;
        }
    }

    private static void CheckKeyUsage(
        X509Certificate2 certificate,
        CertificateVerificationOptions options,
        ref CertificateVerificationError errors,
        List<string> diagnostics)
    {
        if (options.ExpectedPurpose is not { } purpose)
        {
            return;
        }

        var (allOf, anyOf) = ExpectedKeyUsage(purpose);

        X509KeyUsageExtension? extension = null;
        foreach (var ext in certificate.Extensions)
        {
            if (ext is X509KeyUsageExtension keyUsage)
            {
                extension = keyUsage;
                break;
            }
        }

        if (extension is null)
        {
            // Strict: EBICS certificates are expected to carry a KeyUsage extension. Gated by the
            // Spec-Vorbehalt on ExpectedKeyUsage.
            errors |= CertificateVerificationError.InvalidKeyUsage;
            diagnostics.Add($"Certificate has no KeyUsage extension (expected for {purpose}).");
            return;
        }

        var usages = extension.KeyUsages;
        var allPresent = (usages & allOf) == allOf;
        var anyPresent = anyOf == X509KeyUsageFlags.None || (usages & anyOf) != 0;
        if (!allPresent || !anyPresent)
        {
            errors |= CertificateVerificationError.InvalidKeyUsage;
            diagnostics.Add($"KeyUsage {usages} does not satisfy the EBICS profile for {purpose}.");
        }
    }

    private static void CheckPublicKey(
        X509Certificate2 certificate,
        CertificateVerificationOptions options,
        ref CertificateVerificationError errors,
        List<string> diagnostics)
    {
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is null)
        {
            errors |= CertificateVerificationError.NotRsa;
            diagnostics.Add("Certificate does not carry an RSA public key (EBICS keys are RSA).");
            return;
        }

        if (options.ExpectedPublicKey is not { } expected)
        {
            return;
        }

        var actual = RsaKeyMaterial.FromPublicKey(rsa);
        var matches = expected.Modulus.Span.SequenceEqual(actual.Modulus.Span)
            && expected.Exponent.Span.SequenceEqual(actual.Exponent.Span);
        if (!matches)
        {
            errors |= CertificateVerificationError.KeyMismatch;
            diagnostics.Add("Certificate public key does not match the expected subscriber key.");
        }
    }

    /// <summary>
    /// Maps an EBICS <see cref="KeyPurpose"/> to the <see cref="X509KeyUsageFlags"/> its certificate
    /// is expected to assert: <c>AllOf</c> bits must all be present, and — when <c>AnyOf</c> is not
    /// <see cref="X509KeyUsageFlags.None"/> — at least one <c>AnyOf</c> bit must be present.
    /// </summary>
    /// <remarks>
    /// <b>⚠️ Spec-Vorbehalt:</b> the EBICS X.509 key-usage profile per purpose is not yet verified
    /// against the official EBICS Annex (see <c>CLAUDE.md</c>). This is the one place to adjust it.
    /// Signature and authentication certificates are required to assert <c>DigitalSignature</c>;
    /// <c>NonRepudiation</c> is permitted but not required for signature (tighten by moving it into
    /// <c>AllOf</c> here). Encryption certificates must assert <c>KeyEncipherment</c> or
    /// <c>DataEncipherment</c>. Extended Key Usage is deliberately not checked (EBICS defines no
    /// standard EKU OIDs for A/E/X keys).
    /// </remarks>
    private static (X509KeyUsageFlags AllOf, X509KeyUsageFlags AnyOf) ExpectedKeyUsage(KeyPurpose purpose) => purpose switch
    {
        KeyPurpose.Signature => (X509KeyUsageFlags.DigitalSignature, X509KeyUsageFlags.None),
        KeyPurpose.Authentication => (X509KeyUsageFlags.DigitalSignature, X509KeyUsageFlags.None),
        KeyPurpose.Encryption => (X509KeyUsageFlags.None, X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment),
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown key purpose."),
    };
}
