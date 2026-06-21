using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace EBICO.Core.Serialization;

/// <summary>
/// Produces the W3C canonical form (C14N) of XML — the byte sequence over which EBICS
/// authentication signatures compute their digests. Both the inclusive
/// (<em>Canonical XML 1.0</em>) and the exclusive (<em>Exclusive XML Canonicalization 1.0</em>)
/// families are supported, selected through <see cref="C14nMode"/>; the default is
/// <see cref="C14nMode.Inclusive"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the whitespace-tolerant test helper <c>CanonicalXmlComparer</c>, this canonicalizer
/// is <em>whitespace-faithful</em>: it loads documents with <c>PreserveWhitespace = true</c>,
/// because the canonical octets are the signed material and must reflect the document exactly
/// as it is. The output is always a UTF-8 octet stream returned as <see cref="byte"/>[].
/// </para>
/// <para>
/// Input parsing is hardened against DTD/XXE attacks (<see cref="DtdProcessing.Prohibit"/>,
/// no <see cref="XmlResolver"/>) — a <c>&lt;!DOCTYPE&gt;</c> is rejected with an
/// <see cref="XmlException"/>. Selecting <em>which</em> nodes to sign (e.g. the EBICS
/// <c>authenticate="true"</c> elements) is a signature concern (M2) and lives in the caller;
/// this type only canonicalizes what it is given.
/// </para>
/// </remarks>
public static class XmlCanonicalizer
{
    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        // Whitespace, comments and processing instructions are intentionally preserved at
        // the reader level: the C14N transform itself decides what the canonical form keeps.
    };

    /// <summary>
    /// Canonicalizes the well-formed document <paramref name="xml"/> and returns the canonical
    /// form as UTF-8 octets.
    /// </summary>
    /// <param name="xml">The XML document to canonicalize.</param>
    /// <param name="mode">The canonicalization variant. Defaults to <see cref="C14nMode.Inclusive"/>.</param>
    /// <param name="inclusiveNamespacePrefixList">
    /// Optional space-separated prefix list for the exclusive modes (the <c>InclusiveNamespaces</c>
    /// <c>PrefixList</c>); ignored by the inclusive modes.
    /// </param>
    /// <returns>The canonical form as a UTF-8 byte sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="XmlException">
    /// <paramref name="xml"/> is not well-formed, or contains a prohibited <c>&lt;!DOCTYPE&gt;</c>.
    /// </exception>
    public static byte[] Canonicalize(
        string xml,
        C14nMode mode = C14nMode.Inclusive,
        string? inclusiveNamespacePrefixList = null)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(xml), CreateReaderSettings()))
        {
            document.Load(reader);
        }

        return Canonicalize(document, mode, inclusiveNamespacePrefixList);
    }

    /// <summary>
    /// Canonicalizes an already-parsed <paramref name="document"/> and returns the canonical
    /// form as UTF-8 octets. The caller is responsible for having parsed the document safely
    /// (and with <c>PreserveWhitespace = true</c> when the original whitespace is significant).
    /// </summary>
    /// <param name="document">The document to canonicalize.</param>
    /// <param name="mode">The canonicalization variant. Defaults to <see cref="C14nMode.Inclusive"/>.</param>
    /// <param name="inclusiveNamespacePrefixList">See <see cref="Canonicalize(string, C14nMode, string?)"/>.</param>
    /// <returns>The canonical form as a UTF-8 byte sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public static byte[] Canonicalize(
        XmlDocument document,
        C14nMode mode = C14nMode.Inclusive,
        string? inclusiveNamespacePrefixList = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var transform = CreateTransform(mode, inclusiveNamespacePrefixList);
        transform.LoadInput(document);
        return ReadOutput(transform);
    }

    /// <summary>
    /// Canonicalizes a node-set (e.g. the elements selected for an EBICS authentication
    /// signature) and returns the canonical form as UTF-8 octets.
    /// </summary>
    /// <param name="nodes">The nodes to canonicalize, typically obtained from a parsed document.</param>
    /// <param name="mode">The canonicalization variant. Defaults to <see cref="C14nMode.Inclusive"/>.</param>
    /// <param name="inclusiveNamespacePrefixList">See <see cref="Canonicalize(string, C14nMode, string?)"/>.</param>
    /// <returns>The canonical form as a UTF-8 byte sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public static byte[] Canonicalize(
        XmlNodeList nodes,
        C14nMode mode = C14nMode.Inclusive,
        string? inclusiveNamespacePrefixList = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var transform = CreateTransform(mode, inclusiveNamespacePrefixList);
        transform.LoadInput(nodes);
        return ReadOutput(transform);
    }

    /// <summary>
    /// Convenience overload of <see cref="Canonicalize(string, C14nMode, string?)"/> that decodes
    /// the canonical UTF-8 octets to a <see cref="string"/>.
    /// </summary>
    /// <param name="xml">The XML document to canonicalize.</param>
    /// <param name="mode">The canonicalization variant. Defaults to <see cref="C14nMode.Inclusive"/>.</param>
    /// <param name="inclusiveNamespacePrefixList">See <see cref="Canonicalize(string, C14nMode, string?)"/>.</param>
    /// <returns>The canonical form as a UTF-8 string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="XmlException">
    /// <paramref name="xml"/> is not well-formed, or contains a prohibited <c>&lt;!DOCTYPE&gt;</c>.
    /// </exception>
    public static string CanonicalizeToString(
        string xml,
        C14nMode mode = C14nMode.Inclusive,
        string? inclusiveNamespacePrefixList = null)
        => Encoding.UTF8.GetString(Canonicalize(xml, mode, inclusiveNamespacePrefixList));

    private static Transform CreateTransform(C14nMode mode, string? inclusiveNamespacePrefixList)
    {
        Transform transform = mode switch
        {
            C14nMode.Inclusive => new XmlDsigC14NTransform(includeComments: false),
            C14nMode.InclusiveWithComments => new XmlDsigC14NTransform(includeComments: true),
            C14nMode.Exclusive => new XmlDsigExcC14NTransform { InclusiveNamespacesPrefixList = inclusiveNamespacePrefixList },
            C14nMode.ExclusiveWithComments => new XmlDsigExcC14NWithCommentsTransform { InclusiveNamespacesPrefixList = inclusiveNamespacePrefixList },
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown canonicalization mode."),
        };

        // No external resolution of anything the transform might encounter (hardening).
        transform.Resolver = null;
        return transform;
    }

    private static byte[] ReadOutput(Transform transform)
    {
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var buffer = new MemoryStream();
        output.CopyTo(buffer);
        return buffer.ToArray();
    }
}
