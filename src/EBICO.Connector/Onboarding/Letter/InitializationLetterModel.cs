using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding.Letter;

/// <summary>Which onboarding letter is being rendered.</summary>
public enum LetterKind
{
    /// <summary>The INI letter, listing the bank-technical signature key (<c>A00x</c>).</summary>
    Ini,

    /// <summary>The HIA letter, listing the authentication (<c>X00x</c>) and encryption (<c>E00x</c>) keys.</summary>
    Hia,
}

/// <summary>One key row on an initialization letter.</summary>
/// <param name="Purpose">The key purpose (signature/authentication/encryption).</param>
/// <param name="KeyVersion">The key version code (e.g. <c>"A005"</c>).</param>
/// <param name="FingerprintText">
/// The SHA-256 fingerprint pre-rendered for the letter (uppercase hex, byte pairs, eight per line —
/// as produced by <see cref="PublicKeyFingerprint.ToLetterFormat"/>).
/// </param>
public sealed record LetterKeyEntry(KeyPurpose Purpose, string KeyVersion, string FingerprintText);

/// <summary>
/// The data for an INI or HIA initialization letter: the subscriber identifiers, the EBICS version,
/// the creation timestamp and one <see cref="LetterKeyEntry"/> per key. It is a pure data record so
/// the renderers stay deterministic and testable (the timestamp is supplied, not read from the clock).
/// </summary>
public sealed record InitializationLetterModel
{
    /// <summary>Which letter this is (INI or HIA).</summary>
    public required LetterKind Kind { get; init; }

    /// <summary>The bank <c>HostID</c>.</summary>
    public required string HostId { get; init; }

    /// <summary>The customer <c>PartnerID</c>.</summary>
    public required string PartnerId { get; init; }

    /// <summary>The subscriber <c>UserID</c>.</summary>
    public required string UserId { get; init; }

    /// <summary>The EBICS protocol version code (e.g. <c>"H005"</c>).</summary>
    public required string VersionCode { get; init; }

    /// <summary>When the letter was created (rendered into the letter for the bank's records).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The keys listed on the letter (one row each).</summary>
    public required IReadOnlyList<LetterKeyEntry> Keys { get; init; }
}
