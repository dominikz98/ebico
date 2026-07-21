using System.Xml.Linq;

namespace EBICO.Tests.Conformance;

/// <summary>
/// Wire-shape mutators that rewrite an EBICS request's XML the way a <em>foreign</em> client legitimately
/// might, without changing what the request means. EBICO's own serializer emits a fixed shape (protocol
/// namespace as the unprefixed default, no indentation, no comments — see
/// <see cref="EBICO.Core.Serialization.EbicsXmlSerializer"/>); a real third-party client is free to choose
/// a different, equally valid encoding. These helpers produce exactly such variations so the conformance
/// suite can assert the server parses them identically.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Reindent"/> and <see cref="InjectComments"/> are <b>canonically equivalent</b> to their
/// input under <c>Canonical XML 1.0</c> (inclusive, comments dropped): insignificant whitespace and
/// comments vanish in the canonical form, so
/// <see cref="EBICO.Tests.Infrastructure.CanonicalXmlComparer.AreEqual"/> holds. <see cref="WithRootPrefix"/>
/// is deliberately <b>not</b> canonically equivalent — moving the protocol namespace from the default onto
/// a prefix changes the canonical octets — which is the point: it proves the server keys on the namespace
/// URI, not on EBICO's own prefix convention.
/// </para>
/// <para>
/// All three preserve element/attribute content and structure, so element text (e.g. the base64
/// <c>OrderData</c>) is never reindented or otherwise altered.
/// </para>
/// </remarks>
internal static class XmlShape
{
    /// <summary>
    /// Re-serializes <paramref name="xml"/> with pretty-print indentation (EBICO emits none). Only
    /// insignificant whitespace between elements is added; text-only element content is left inline.
    /// </summary>
    /// <param name="xml">The request XML to reindent.</param>
    /// <returns>The reindented, canonically equivalent XML.</returns>
    public static string Reindent(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        // LoadOptions.None drops whitespace-only nodes; ToString() then re-indents. An element whose
        // only child is text (base64 OrderData) keeps that text inline, so payloads are untouched.
        var document = XDocument.Parse(xml, LoadOptions.None);
        return document.ToString(SaveOptions.None);
    }

    /// <summary>
    /// Inserts an XML comment as the first child of the root element. Comments are dropped by the
    /// (comment-free) canonical form and ignored by <c>XmlSerializer</c>, so the request's meaning is
    /// unchanged — a client that annotates its output must not trip the server.
    /// </summary>
    /// <param name="xml">The request XML to annotate.</param>
    /// <returns>The annotated, canonically equivalent XML.</returns>
    public static string InjectComments(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var document = XDocument.Parse(xml, LoadOptions.None);
        document.Root!.AddFirst(new XComment(" emitted by a third-party EBICS client "));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Moves the root's protocol namespace off the default declaration onto an explicit
    /// <paramref name="prefix"/>, so every element in that namespace is serialized as
    /// <c>&lt;prefix:element&gt;</c> instead of relying on an inherited default namespace. Semantically
    /// identical (same namespace URI, same names) but a different wire encoding — the shape many
    /// third-party clients emit.
    /// </summary>
    /// <param name="xml">The request XML to reprefix.</param>
    /// <param name="prefix">The namespace prefix to introduce (default <c>eb</c>).</param>
    /// <returns>The reprefixed XML; unchanged when the root has no default namespace.</returns>
    public static string WithRootPrefix(string xml, string prefix = "eb")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        var document = XDocument.Parse(xml, LoadOptions.None);
        var root = document.Root!;
        var ns = root.Name.Namespace;
        if (ns == XNamespace.None)
        {
            return xml;
        }

        // Drop the root's default-namespace declaration and add a prefixed one for the same URI. With no
        // default declaration in scope, LINQ-to-XML serializes every element in that namespace (root and
        // descendants) using the prefix.
        foreach (var declaration in root.Attributes()
            .Where(a => a.IsNamespaceDeclaration && a.Name.LocalName == "xmlns")
            .ToList())
        {
            declaration.Remove();
        }

        if (root.Attribute(XNamespace.Xmlns + prefix) is null)
        {
            root.Add(new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName));
        }

        // xsi:type values are QNames. EBICO emits them unprefixed, so they resolved against the (former)
        // default namespace; with the default gone they must now carry the prefix, or they would bind to
        // no namespace. A conforming prefixed client applies exactly this rewrite.
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        foreach (var typeAttribute in root.DescendantsAndSelf().Attributes(xsi + "type").ToList())
        {
            if (!typeAttribute.Value.Contains(':'))
            {
                typeAttribute.Value = $"{prefix}:{typeAttribute.Value}";
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }
}
