using EBICO.Core.Crypto;

namespace EBICO.Suite.Services;

/// <summary>
/// A public key surfaced to the Suite's key/certificate view (issue #55), together with its
/// precomputed SHA-256 public-key fingerprint in the INI-letter rendering.
/// </summary>
/// <remarks>
/// This is a Suite read-model DTO built on the <see cref="EBICO.Core.Crypto"/> primitives.
/// It carries the public <see cref="RsaKeyMaterial"/> so the INI-letter comparison tool can
/// re-verify a fingerprint against the key, and the ready-to-display <see cref="FingerprintText"/>
/// so the overview table needs no crypto call while rendering.
/// </remarks>
public sealed record KeyView
{
    /// <summary>Human-readable owner label, e.g. <c>"Teilnehmer PARTNER01 / USER0001"</c> or <c>"Bank EBICOHOST"</c>.</summary>
    public required string OwnerLabel { get; init; }

    /// <summary>The EBICS key purpose (signature <c>A</c>, encryption <c>E</c>, authentication <c>X</c>).</summary>
    public required KeyPurpose Purpose { get; init; }

    /// <summary>The four-character key-version code, e.g. <c>"A006"</c>.</summary>
    public required string KeyVersion { get; init; }

    /// <summary>The RSA public key material (public components only).</summary>
    public required RsaKeyMaterial PublicKey { get; init; }

    /// <summary>
    /// The SHA-256 fingerprint rendered for the INI letter — grouped uppercase hex, eight bytes per
    /// line (see <see cref="PublicKeyFingerprint.ToLetterFormat"/>).
    /// </summary>
    public required string FingerprintText { get; init; }
}
