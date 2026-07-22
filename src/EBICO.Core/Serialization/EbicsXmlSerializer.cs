using System.Collections.Concurrent;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Core.Serialization;

/// <summary>
/// Serializes and deserializes EBICS envelopes with <em>deterministic</em>, stable output
/// across H003/H004/H005: UTF-8 without a byte-order mark, no indentation, a fixed namespace
/// prefix map (the version's protocol namespace as the default, <c>ds</c> for XML-DSig) and no
/// stray <c>xsi</c>/<c>xsd</c> declarations. Element and attribute order is already fixed by the
/// generated bindings, so the same graph always serializes to the same bytes.
/// </summary>
/// <remarks>
/// Built on the committed bindings (<c>Schema/{H003,H004,H005}</c>) and the version registry
/// (<see cref="EbicsVersions"/>, <see cref="EbicsVersionDetector"/>). Inbound parsing is hardened
/// against DTD/XXE (<see cref="DtdProcessing.Prohibit"/>, no <see cref="XmlResolver"/>).
/// <see cref="XmlSerializer"/> instances are cached per type because constructing them is
/// expensive. See <c>docs/protocol/serialization-c14n.md</c>.
/// </remarks>
public static class EbicsXmlSerializer
{
    private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

    private static readonly ConcurrentDictionary<Type, XmlSerializer> SerializerCache = new();

    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        OmitXmlDeclaration = false,
        CloseOutput = false,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // --- Serialization ------------------------------------------------------

    /// <summary>
    /// Serializes <paramref name="envelope"/> to deterministic UTF-8 octets. The version is taken
    /// from <see cref="IEbicsEnvelope.ProtocolVersion"/>.
    /// </summary>
    /// <param name="envelope">The envelope to serialize.</param>
    /// <returns>The serialized envelope as UTF-8 bytes (no BOM).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is <see langword="null"/>.</exception>
    public static byte[] SerializeToUtf8Bytes(IEbicsEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return SerializeToBytes(envelope, EbicsVersions.Get(envelope.ProtocolVersion));
    }

    /// <summary>
    /// Serializes an EBICS object <paramref name="graph"/> belonging to <paramref name="version"/>
    /// to deterministic UTF-8 octets, using that version's protocol namespace as the default.
    /// </summary>
    /// <param name="graph">The object graph to serialize (root must be in the version's namespace).</param>
    /// <param name="version">The protocol version whose namespace map to apply.</param>
    /// <returns>The serialized graph as UTF-8 bytes (no BOM).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] SerializeToUtf8Bytes(object graph, EbicsVersion version)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return SerializeToBytes(graph, EbicsVersions.Get(version));
    }

    /// <summary>Serializes <paramref name="envelope"/> and decodes the UTF-8 octets to a string.</summary>
    /// <param name="envelope">The envelope to serialize.</param>
    /// <returns>The serialized envelope as a UTF-8 string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is <see langword="null"/>.</exception>
    public static string SerializeToString(IEbicsEnvelope envelope)
        => Utf8NoBom.GetString(SerializeToUtf8Bytes(envelope));

    /// <summary>Serializes <paramref name="graph"/> and decodes the UTF-8 octets to a string.</summary>
    /// <param name="graph">The object graph to serialize.</param>
    /// <param name="version">The protocol version whose namespace map to apply.</param>
    /// <returns>The serialized graph as a UTF-8 string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static string SerializeToString(object graph, EbicsVersion version)
        => Utf8NoBom.GetString(SerializeToUtf8Bytes(graph, version));

    /// <summary>Serializes <paramref name="envelope"/> to <paramref name="output"/> (not closed).</summary>
    /// <param name="output">The destination stream; left open by this method.</param>
    /// <param name="envelope">The envelope to serialize.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void Serialize(Stream output, IEbicsEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(envelope);
        SerializeToStream(output, envelope, EbicsVersions.Get(envelope.ProtocolVersion));
    }

    /// <summary>Serializes <paramref name="graph"/> to <paramref name="output"/> (not closed).</summary>
    /// <param name="output">The destination stream; left open by this method.</param>
    /// <param name="graph">The object graph to serialize.</param>
    /// <param name="version">The protocol version whose namespace map to apply.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static void Serialize(Stream output, object graph, EbicsVersion version)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(graph);
        SerializeToStream(output, graph, EbicsVersions.Get(version));
    }

    /// <summary>
    /// Serializes a standalone EBICS <em>order-data</em> graph (e.g. the INI
    /// <c>SignaturePubKeyOrderData</c> in the <c>S001</c>/<c>S002</c> namespace, or the HIA/HPB
    /// order data in a protocol namespace) to deterministic UTF-8 octets. Unlike
    /// <see cref="SerializeToUtf8Bytes(object, EbicsVersion)"/> this does <b>not</b> force a protocol
    /// default namespace — the graph's own <c>XmlRoot</c> namespace drives the root, which is what
    /// order-data payloads need (they are not enveloped). Only the XML-DSig <c>ds</c> prefix is fixed
    /// (order data carries <c>ds:X509Data</c>/<c>ds:RSAKeyValue</c>), which also suppresses the stray
    /// <c>xsi</c>/<c>xsd</c> declarations.
    /// </summary>
    /// <param name="graph">The order-data object graph to serialize.</param>
    /// <returns>The serialized graph as UTF-8 bytes (no BOM), ready to compress and base64-encode.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public static byte[] SerializeOrderData(object graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("ds", XmlDsigNamespace);

        using var buffer = new MemoryStream();
        var serializer = GetSerializer(graph.GetType());
        using (var writer = XmlWriter.Create(buffer, WriterSettings))
        {
            serializer.Serialize(writer, graph, namespaces);
        }

        return buffer.ToArray();
    }

    // --- Deserialization ----------------------------------------------------

    /// <summary>Deserializes <paramref name="xml"/> into <typeparamref name="T"/> with XXE-hardened parsing.</summary>
    /// <typeparam name="T">The root binding type.</typeparam>
    /// <param name="xml">The XML to deserialize.</param>
    /// <returns>The deserialized graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    public static T Deserialize<T>(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return (T)DeserializeCore(typeof(T), xml);
    }

    /// <summary>Deserializes <paramref name="xml"/> into <paramref name="rootType"/> with XXE-hardened parsing.</summary>
    /// <param name="rootType">The root binding type to deserialize into.</param>
    /// <param name="xml">The XML to deserialize.</param>
    /// <returns>The deserialized graph.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static object Deserialize(Type rootType, string xml)
    {
        ArgumentNullException.ThrowIfNull(rootType);
        ArgumentNullException.ThrowIfNull(xml);
        return DeserializeCore(rootType, xml);
    }

    /// <summary>
    /// Deserializes a raw EBICS envelope, dispatching on its version and root element: the root
    /// namespace selects the version (via <see cref="EbicsVersionDetector"/>, which knows the H003
    /// legacy namespace) and the root element name selects which of the six envelope bindings to use.
    /// </summary>
    /// <param name="xml">The raw envelope XML.</param>
    /// <returns>The deserialized envelope as an <see cref="IEbicsEnvelope"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="xml"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsEnvelopeFormatException">
    /// <paramref name="xml"/> is empty/malformed, contains a prohibited <c>&lt;!DOCTYPE&gt;</c>, its
    /// root element is not one of the six recognized EBICS envelopes, or the document is well-formed
    /// but cannot be mapped onto the version's binding.
    /// </exception>
    /// <exception cref="EbicsVersionNotSupportedException">
    /// The root namespace does not belong to any supported version.
    /// </exception>
    public static IEbicsEnvelope DeserializeEnvelope(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var info = EbicsVersionDetector.Detect(xml);
        var rootLocalName = ReadRootLocalName(xml);
        var rootType = ResolveEnvelopeType(info, rootLocalName);

        try
        {
            return (IEbicsEnvelope)DeserializeCore(rootType, xml);
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException)
        {
            // At this boundary the octets are, by definition, inbound client input: a recognized root
            // element whose content the XmlSerializer cannot map (an unparsable dateTime/hexBinary, a
            // missing xsi:type discriminator on an abstract binding, …) is the client's invalid XML,
            // never a server fault. Translating it here — rather than widening the server's error
            // mapper — keeps the classification at the only place that knows whose bytes these are,
            // and makes it EBICS_INVALID_XML instead of EBICS_INTERNAL_ERROR (issue #117).
            //
            // Deliberately *not* in DeserializeCore: the Deserialize<T> overloads also decode order
            // data, where the server's OrderDataFault already maps InvalidOperationException to
            // EBICS_INVALID_ORDER_DATA_FORMAT — a translation here would override that mapping.
            throw new EbicsEnvelopeFormatException(
                $"The EBICS {info.Code} '{rootLocalName}' envelope is well-formed but does not match the "
                + "expected schema.",
                ex);
        }
    }

    // --- Internals ----------------------------------------------------------

    private static byte[] SerializeToBytes(object graph, EbicsVersionInfo info)
    {
        using var buffer = new MemoryStream();
        SerializeToStream(buffer, graph, info);
        return buffer.ToArray();
    }

    private static void SerializeToStream(Stream output, object graph, EbicsVersionInfo info)
    {
        var serializer = GetSerializer(graph.GetType());
        using var writer = XmlWriter.Create(output, WriterSettings);
        serializer.Serialize(writer, graph, BuildNamespaces(info));
    }

    private static object DeserializeCore(Type rootType, string xml)
    {
        var serializer = GetSerializer(rootType);
        using var reader = XmlReader.Create(new StringReader(xml), CreateReaderSettings());
        return serializer.Deserialize(reader)!;
    }

    private static XmlSerializer GetSerializer(Type type) =>
        SerializerCache.GetOrAdd(type, static t => new XmlSerializer(t));

    private static XmlSerializerNamespaces BuildNamespaces(EbicsVersionInfo info)
    {
        var namespaces = new XmlSerializerNamespaces();
        // The protocol namespace as the default (unprefixed root) and a stable `ds` for XML-DSig.
        // Passing a non-empty set also suppresses the serializer's automatic xsi/xsd declarations.
        namespaces.Add(string.Empty, info.NamespaceUri);
        namespaces.Add("ds", XmlDsigNamespace);
        return namespaces;
    }

    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private static string ReadRootLocalName(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), CreateReaderSettings());
        try
        {
            if (reader.MoveToContent() == XmlNodeType.Element)
            {
                return reader.LocalName;
            }
        }
        catch (XmlException)
        {
            // Detect() already validated well-formedness; fall through to the "unrecognized" path.
        }

        return string.Empty;
    }

    private static Type ResolveEnvelopeType(EbicsVersionInfo info, string rootLocalName) => rootLocalName switch
    {
        "ebicsRequest" => info.RequestType,
        "ebicsResponse" => info.ResponseType,
        "ebicsUnsecuredRequest" => info.UnsecuredRequestType,
        "ebicsUnsignedRequest" => info.UnsignedRequestType,
        "ebicsNoPubKeyDigestsRequest" => info.NoPubKeyDigestsRequestType,
        "ebicsKeyManagementResponse" => info.KeyManagementResponseType,
        _ => throw new EbicsEnvelopeFormatException(
            $"Root element '{rootLocalName}' is not a recognized EBICS {info.Code} envelope."),
    };
}
