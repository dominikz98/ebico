using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace EBICO.Tests.Infrastructure;

/// <summary>
/// Compares XML by canonicalizing both inputs with <em>Canonical XML 1.0</em>
/// (the canonicalization EBICS XML signatures rely on) and comparing the result.
/// <para>
/// Insensitive to insignificant whitespace/indentation, attribute ordering and
/// namespace-declaration ordering; sensitive to element/attribute content and
/// document structure. Intended as a test helper for protocol/serialization
/// tests — the production C14N implementation arrives with the Core protocol
/// work (M1, issue #15).
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
        var document = new XmlDocument { PreserveWhitespace = false };
        document.LoadXml(xml);

        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(document);

        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var reader = new StreamReader(output, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="left"/> and <paramref name="right"/>
    /// are canonically equal.
    /// </summary>
    public static bool AreEqual(string left, string right)
        => string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.Ordinal);
}
