using System.Text.RegularExpressions;

namespace EBICO.Core.Crypto;

/// <summary>
/// A validated EBICS key-version code: a four-character string of the form
/// <c>letter + three digits</c>, where the letter is <c>A</c> (signature), <c>E</c>
/// (encryption) or <c>X</c> (authentication) — e.g. <c>"A005"</c>, <c>"E002"</c>,
/// <c>"X002"</c>. Mirrors the schema patterns <c>A\d{3}</c> / <c>E\d{3}</c> / <c>X\d{3}</c>
/// carried in the <c>SignatureVersion</c> / <c>EncryptionVersion</c> /
/// <c>AuthenticationVersion</c> elements.
/// </summary>
/// <remarks>
/// <para>
/// Obtain instances via <see cref="Create(string)"/> or <see cref="TryCreate(string?, out KeyVersion)"/>.
/// Validation here checks only the <i>shape</i>; a well-formed but unknown code (e.g.
/// <c>"A999"</c>) is accepted. Use <see cref="KeyVersions"/> to resolve a code to its
/// known metadata and to apply per-EBICS-version policy.
/// </para>
/// <para>
/// As a <see langword="struct"/>, <c>default(KeyVersion)</c> bypasses the factory and has a
/// <see langword="null"/> <see cref="Value"/>; valid instances are produced only via the factories.
/// </para>
/// </remarks>
public readonly partial record struct KeyVersion
{
    [GeneratedRegex(@"^[AEX]\d{3}$")]
    private static partial Regex Pattern();

    private KeyVersion(string value, KeyPurpose purpose)
    {
        Value = value;
        Purpose = purpose;
    }

    /// <summary>The validated four-character code (e.g. <c>"A005"</c>).</summary>
    public string Value { get; }

    /// <summary>The purpose implied by the leading letter of <see cref="Value"/>.</summary>
    public KeyPurpose Purpose { get; }

    /// <summary>Creates a validated <see cref="KeyVersion"/> from its code.</summary>
    /// <param name="value">The raw key-version code.</param>
    /// <returns>The validated <see cref="KeyVersion"/>.</returns>
    /// <exception cref="InvalidKeyVersionException">
    /// <paramref name="value"/> is <see langword="null"/> or not a letter <c>A</c>/<c>E</c>/<c>X</c>
    /// followed by exactly three digits.
    /// </exception>
    public static KeyVersion Create(string value)
    {
        if (value is null)
        {
            throw new InvalidKeyVersionException("Key version must not be null.");
        }

        if (!Pattern().IsMatch(value))
        {
            throw new InvalidKeyVersionException(
                $"Key version '{value}' is not well-formed; expected a letter A/E/X followed by three digits (e.g. \"A005\").");
        }

        // The leading character is guaranteed to be A/E/X by the pattern.
        _ = KeyPurposeExtensions.TryFromLetter(value[0], out var purpose);
        return new KeyVersion(value, purpose);
    }

    /// <summary>Tries to create a validated <see cref="KeyVersion"/> without throwing.</summary>
    /// <param name="value">The raw key-version code.</param>
    /// <param name="version">The validated value when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a well-formed key-version code.</returns>
    public static bool TryCreate(string? value, out KeyVersion version)
    {
        if (value is not null && Pattern().IsMatch(value))
        {
            _ = KeyPurposeExtensions.TryFromLetter(value[0], out var purpose);
            version = new KeyVersion(value, purpose);
            return true;
        }

        version = default;
        return false;
    }

    /// <summary>Returns the underlying code.</summary>
    /// <returns>The <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}

/// <summary>
/// The RSA scheme an EBICS key version is associated with. This is descriptive
/// <b>metadata only</b> — the key layer of issue #18 does not perform any signing or
/// encryption; the actual operations live in the later M2 issues (#19–#21).
/// </summary>
public enum RsaPaddingScheme
{
    /// <summary>RSASSA-PKCS1-v1_5 (signature versions A004/A005, authentication versions X001/X002).</summary>
    Pkcs1V15,

    /// <summary>RSASSA-PSS (signature version A006).</summary>
    Pss,

    /// <summary>RSAES-OAEP (encryption version E002).</summary>
    Oaep,

    /// <summary>RSAES-PKCS1-v1_5 (legacy encryption version E001).</summary>
    Pkcs1V15Encryption,
}
