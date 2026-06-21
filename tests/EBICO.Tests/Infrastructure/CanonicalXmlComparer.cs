using System.Text;
using System.Xml;
using EBICO.Core.Serialization;

namespace EBICO.Tests.Infrastructure;

/// <summary>
/// Compares XML by canonicalizing both inputs with <em>Canonical XML 1.0</em>
/// (the canonicalization EBICS XML signatures rely on) and comparing the result.
/// <para>
/// Insensitive to insignificant whitespace/indentation, attribute ordering and
/// namespace-declaration ordering; sensitive to element/attribute content and
/// document structure. This is a <b>test helper</b>: it delegates the canonical
/// form to the production canonicalizer
/// (<see cref="XmlCanonicalizer"/>, issue #15, inclusive C14N 1.0) and additionally
/// drops insignificant whitespace so that pure formatting differences compare equal.
/// </para>
/// </summary>
public static class CanonicalXmlComparer
{
    /// <summary>
    /// Canonicalizes <paramref name="xml"/> (Canonical XML 1.0) and returns the
    /// canonical form as a UTF-8 string.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <c>null</c>.</exception>
    /// <exception cref="XmlException"><paramref name="xml"/> is not well-formed.</exception>
    public static string Canonicalize(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        // PreserveWhitespace = false drops whitespace-only text nodes, so pure
        // formatting differences (indentation) do not affect the canonical form.
        // The canonical octets themselves come from the production canonicalizer.
        var document = new XmlDocument { PreserveWhitespace = false };
        document.LoadXml(xml);

        return Encoding.UTF8.GetString(XmlCanonicalizer.Canonicalize(document, C14nMode.Inclusive));
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="left"/> and <paramref name="right"/>
    /// are canonically equal.
    /// </summary>
    public static bool AreEqual(string left, string right)
        => string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.Ordinal);
}
