using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EBICO.Core.Versioning;

namespace EBICO.Core.Administrative;

/// <summary>
/// Renders the machine-readable customer protocol (<c>HAC</c>, issue #41) as XML. HAC is a pure projection
/// over the event log: the customer-visible events are mapped to <see cref="CustomerProtocolEntry"/> by the
/// server and rendered here into an <c>HACResponseOrderData</c> document in the requested version's protocol
/// namespace.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> EBICS defines HAC via a proprietary schema (a <c>camt.086</c>/<c>pain.002</c>
/// derived acknowledgement format, versioned differently across H004/H005) which is not available in the
/// repository (licence). This builder therefore emits a structurally plausible, self-describing projection of
/// the events — one <c>ProtocolEntry</c> per event — not the wire-exact HAC layout. The exact element/attribute
/// mapping must be verified against the official EBICS annexes once the schema is available.
/// </remarks>
public static class HacProtocolBuilder
{
    /// <summary>
    /// Builds the HAC XML order data for <paramref name="version"/> from the customer-visible protocol
    /// <paramref name="entries"/> (ordered by sequence).
    /// </summary>
    /// <param name="version">The protocol version whose namespace the document is emitted in.</param>
    /// <param name="entries">The customer-visible protocol entries to render (already filtered per customer).</param>
    /// <returns>The HAC document as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] Build(EbicsVersion version, IReadOnlyList<CustomerProtocolEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        XNamespace ns = EbicsVersions.Get(version).NamespaceUri;

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                ns + "HACResponseOrderData",
                entries.Select(entry => BuildEntry(ns, entry))));

        return Serialize(document);
    }

    private static XElement BuildEntry(XNamespace ns, CustomerProtocolEntry entry)
    {
        var element = new XElement(
            ns + "ProtocolEntry",
            new XAttribute("sequence", entry.Sequence.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("timestamp", entry.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            new XAttribute("severity", entry.Severity));

        if (!string.IsNullOrEmpty(entry.OrderType))
        {
            element.Add(new XElement(ns + "OrderType", entry.OrderType));
        }

        if (!string.IsNullOrEmpty(entry.ReturnCode))
        {
            var returnCode = new XElement(ns + "ReturnCode", entry.ReturnCode);
            if (!string.IsNullOrEmpty(entry.SymbolicName))
            {
                returnCode.Add(new XAttribute("symbolic", entry.SymbolicName));
            }

            element.Add(returnCode);
        }

        element.Add(new XElement(ns + "Message", entry.Message));
        return element;
    }

    private static byte[] Serialize(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
        };

        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }

        return buffer.ToArray();
    }
}
