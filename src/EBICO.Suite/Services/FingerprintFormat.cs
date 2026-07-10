using System.Diagnostics.CodeAnalysis;

namespace EBICO.Suite.Services;

/// <summary>
/// Parsing helpers for the human-facing public-key fingerprint text (issue #55): the grouped
/// uppercase hex an operator reads off an INI letter (see
/// <see cref="EBICO.Core.Crypto.PublicKeyFingerprint.ToLetterFormat"/>). Turns such free-form input
/// — arbitrary whitespace, upper/lower case — back into the raw digest bytes for comparison.
/// </summary>
public static class FingerprintFormat
{
    /// <summary>Removes all whitespace (spaces, tabs, line breaks) from a fingerprint string.</summary>
    /// <param name="input">The raw input; <see langword="null"/> is treated as empty.</param>
    /// <returns>The input with every whitespace character removed.</returns>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
    }

    /// <summary>
    /// Tries to parse a fingerprint typed from an INI letter (hexadecimal, whitespace allowed,
    /// case-insensitive) into its raw digest bytes.
    /// </summary>
    /// <param name="input">The fingerprint text.</param>
    /// <param name="digest">The parsed bytes when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the input is non-empty, even-length, valid hex.</returns>
    public static bool TryParseHex(string? input, [NotNullWhen(true)] out byte[]? digest)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0 || normalized.Length % 2 != 0)
        {
            digest = null;
            return false;
        }

        try
        {
            digest = Convert.FromHexString(normalized);
            return true;
        }
        catch (FormatException)
        {
            digest = null;
            return false;
        }
    }
}
