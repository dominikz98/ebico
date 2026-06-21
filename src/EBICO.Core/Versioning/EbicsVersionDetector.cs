using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace EBICO.Core.Versioning;

/// <summary>
/// Detects the EBICS protocol version of a raw envelope by inspecting only its root
/// element, without deserializing the whole document. This is the entry point of the
/// server's inbound protocol dispatch: given the bytes of an incoming request it
/// resolves which version's bindings (<see cref="EbicsVersions"/>) should handle it.
/// </summary>
/// <remarks>
/// <para>
/// The discriminator is the <em>root element's namespace URI</em>, resolved through
/// <see cref="EbicsVersions.TryFromNamespace(string?, out EbicsVersionInfo?)"/> (which
/// knows the H003 legacy-namespace special case). The on-the-wire <c>@Version</c>
/// attribute is free text and is therefore ignored by default — the namespace is
/// authoritative because it determines which schema actually applies. Callers that
/// want to reject envelopes whose declared <c>@Version</c> disagrees with their
/// namespace can opt into the strict path.
/// </para>
/// <para>
/// Only the opening root tag is read. A document whose root start tag is well-formed
/// but whose body is incomplete is still detected — completeness of the body is a
/// downstream deserialization concern, not a detection concern. XML parsing is hardened
/// against DTD/XXE attacks. See <c>docs/protocol/version-dispatch.md</c> and ADR-0004.
/// </para>
/// </remarks>
public static class EbicsVersionDetector
{
    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    /// <summary>
    /// Detects the EBICS version of <paramref name="xml"/> leniently (the wire
    /// <c>@Version</c> attribute is ignored; the root namespace decides).
    /// </summary>
    /// <param name="xml">The raw envelope XML.</param>
    /// <returns>The metadata of the detected version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsEnvelopeFormatException">
    /// <paramref name="xml"/> is empty or not well-formed enough to expose a root element.
    /// </exception>
    /// <exception cref="EbicsVersionNotSupportedException">
    /// The root namespace does not belong to any supported version.
    /// </exception>
    public static EbicsVersionInfo Detect(string xml) => Detect(xml, strict: false);

    /// <summary>
    /// Detects the EBICS version of <paramref name="xml"/>, optionally enforcing that
    /// the root <c>@Version</c> attribute agrees with the root namespace.
    /// </summary>
    /// <param name="xml">The raw envelope XML.</param>
    /// <param name="strict">
    /// When <see langword="true"/>, a present <c>@Version</c> attribute that disagrees
    /// with the namespace-derived version code raises <see cref="EbicsVersionMismatchException"/>.
    /// An absent <c>@Version</c> attribute is never treated as a mismatch.
    /// </param>
    /// <returns>The metadata of the detected version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsEnvelopeFormatException">
    /// <paramref name="xml"/> is empty or not well-formed enough to expose a root element.
    /// </exception>
    /// <exception cref="EbicsVersionNotSupportedException">
    /// The root namespace does not belong to any supported version.
    /// </exception>
    /// <exception cref="EbicsVersionMismatchException">
    /// <paramref name="strict"/> is <see langword="true"/> and the <c>@Version</c>
    /// attribute disagrees with the namespace-derived version code.
    /// </exception>
    public static EbicsVersionInfo Detect(string xml, bool strict)
    {
        ArgumentNullException.ThrowIfNull(xml);

        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new EbicsEnvelopeFormatException("The EBICS envelope is empty.");
        }

        using var reader = XmlReader.Create(new StringReader(xml), CreateReaderSettings());
        return DetectCore(reader, strict);
    }

    /// <summary>
    /// Detects the EBICS version from a stream. The stream is read only as far as the
    /// root element and is <em>not</em> closed (the caller retains ownership).
    /// </summary>
    /// <param name="xml">A stream positioned at the start of the envelope XML.</param>
    /// <param name="strict">See <see cref="Detect(string, bool)"/>.</param>
    /// <returns>The metadata of the detected version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsEnvelopeFormatException">
    /// <paramref name="xml"/> is not well-formed enough to expose a root element.
    /// </exception>
    /// <exception cref="EbicsVersionNotSupportedException">
    /// The root namespace does not belong to any supported version.
    /// </exception>
    /// <exception cref="EbicsVersionMismatchException">
    /// <paramref name="strict"/> is <see langword="true"/> and the <c>@Version</c>
    /// attribute disagrees with the namespace-derived version code.
    /// </exception>
    public static EbicsVersionInfo Detect(Stream xml, bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var reader = XmlReader.Create(xml, CreateReaderSettings());
        return DetectCore(reader, strict);
    }

    /// <summary>
    /// Non-throwing lenient detection: returns <see langword="false"/> for any empty,
    /// malformed or unsupported envelope instead of raising an
    /// <see cref="EbicsVersionException"/>.
    /// </summary>
    /// <param name="xml">The raw envelope XML.</param>
    /// <param name="info">The detected version metadata when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a supported version was detected.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    public static bool TryDetect(string xml, [NotNullWhen(true)] out EbicsVersionInfo? info)
    {
        ArgumentNullException.ThrowIfNull(xml);

        try
        {
            info = Detect(xml, strict: false);
            return true;
        }
        catch (EbicsVersionException)
        {
            info = null;
            return false;
        }
    }

    /// <summary>
    /// Non-throwing lenient detection from a stream. The stream is not closed.
    /// </summary>
    /// <param name="xml">A stream positioned at the start of the envelope XML.</param>
    /// <param name="info">The detected version metadata when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a supported version was detected.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    public static bool TryDetect(Stream xml, [NotNullWhen(true)] out EbicsVersionInfo? info)
    {
        ArgumentNullException.ThrowIfNull(xml);

        try
        {
            info = Detect(xml, strict: false);
            return true;
        }
        catch (EbicsVersionException)
        {
            info = null;
            return false;
        }
    }

    private static EbicsVersionInfo DetectCore(XmlReader reader, bool strict)
    {
        XmlNodeType nodeType;
        string namespaceUri;
        string? versionAttribute;
        try
        {
            nodeType = reader.MoveToContent();
            if (nodeType != XmlNodeType.Element)
            {
                throw new EbicsEnvelopeFormatException("The EBICS envelope has no root element.");
            }

            namespaceUri = reader.NamespaceURI;
            versionAttribute = reader.GetAttribute("Version");
        }
        catch (XmlException ex)
        {
            throw new EbicsEnvelopeFormatException("The EBICS envelope is not well-formed XML.", ex);
        }

        if (!EbicsVersions.TryFromNamespace(namespaceUri, out var info))
        {
            throw new EbicsVersionNotSupportedException(
                $"Unsupported EBICS root namespace '{namespaceUri}'.");
        }

        if (strict && versionAttribute is not null && !string.Equals(versionAttribute, info.Code, StringComparison.Ordinal))
        {
            throw new EbicsVersionMismatchException(
                $"Root namespace '{namespaceUri}' maps to {info.Code}, but the @Version attribute declares '{versionAttribute}'.");
        }

        return info;
    }
}
