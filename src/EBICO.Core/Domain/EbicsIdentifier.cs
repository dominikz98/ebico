using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace EBICO.Core.Domain;

/// <summary>
/// Shared validation for the textual EBICS identifiers (<c>HostID</c>, <c>PartnerID</c>,
/// <c>UserID</c>, <c>SystemID</c>). All four share the same schema constraint —
/// 1–35 characters drawn from <c>[a-zA-Z0-9,=]</c> — so the rule lives in one place
/// rather than being duplicated across the four value objects.
/// </summary>
internal static partial class EbicsIdentifier
{
    /// <summary>Maximum length of an EBICS identifier as defined by the schemas.</summary>
    internal const int MaxLength = 35;

    [GeneratedRegex("^[a-zA-Z0-9,=]{1,35}$")]
    private static partial Regex Pattern();

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is a non-null string
    /// matching the EBICS identifier pattern.
    /// </summary>
    internal static bool IsValid([NotNullWhen(true)] string? value)
        => value is not null && Pattern().IsMatch(value);

    /// <summary>
    /// Returns <paramref name="value"/> unchanged when it is a valid EBICS identifier,
    /// otherwise throws.
    /// </summary>
    /// <param name="value">The candidate identifier.</param>
    /// <param name="identifierName">The kind of identifier, used in the error message (e.g. <c>"HostId"</c>).</param>
    /// <returns>The validated identifier.</returns>
    /// <exception cref="InvalidEbicsIdentifierException">
    /// <paramref name="value"/> is <see langword="null"/>, empty, longer than
    /// <see cref="MaxLength"/> characters, or contains characters outside <c>[a-zA-Z0-9,=]</c>.
    /// </exception>
    internal static string Validate(string? value, string identifierName)
    {
        if (value is null)
        {
            throw new InvalidEbicsIdentifierException($"{identifierName} must not be null.");
        }

        if (!Pattern().IsMatch(value))
        {
            throw new InvalidEbicsIdentifierException(
                $"{identifierName} '{value}' is not a valid EBICS identifier; it must be 1–{MaxLength} characters matching [a-zA-Z0-9,=].");
        }

        return value;
    }
}
