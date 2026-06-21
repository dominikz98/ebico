namespace EBICO.Core.Versioning;

/// <summary>
/// Immutable metadata describing one supported EBICS protocol version: its
/// <see cref="EbicsVersion"/> value, the four-character schema code (e.g. <c>"H005"</c>),
/// the XML namespace URI of its protocol root elements, and the CLR types of the
/// generated envelope bindings for that version.
/// </summary>
public sealed class EbicsVersionInfo
{
    internal EbicsVersionInfo(
        EbicsVersion version,
        string code,
        string namespaceUri,
        Type requestType,
        Type responseType,
        Type unsecuredRequestType,
        Type unsignedRequestType,
        Type noPubKeyDigestsRequestType,
        Type keyManagementResponseType)
    {
        Version = version;
        Code = code;
        NamespaceUri = namespaceUri;
        RequestType = requestType;
        ResponseType = responseType;
        UnsecuredRequestType = unsecuredRequestType;
        UnsignedRequestType = unsignedRequestType;
        NoPubKeyDigestsRequestType = noPubKeyDigestsRequestType;
        KeyManagementResponseType = keyManagementResponseType;
    }

    /// <summary>The protocol version this metadata describes.</summary>
    public EbicsVersion Version { get; }

    /// <summary>
    /// The four-character schema family code as it appears in the root
    /// <c>@Version</c> attribute (e.g. <c>"H005"</c>).
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// The XML namespace URI of this version's protocol root elements. Note that
    /// H003 uses the legacy form <c>http://www.ebics.org/H003</c>, whereas H004/H005
    /// use <c>urn:org:ebics:H00x</c>.
    /// </summary>
    public string NamespaceUri { get; }

    /// <summary>CLR type of the <c>ebicsRequest</c> envelope binding for this version.</summary>
    public Type RequestType { get; }

    /// <summary>CLR type of the <c>ebicsResponse</c> envelope binding for this version.</summary>
    public Type ResponseType { get; }

    /// <summary>CLR type of the <c>ebicsUnsecuredRequest</c> envelope binding for this version.</summary>
    public Type UnsecuredRequestType { get; }

    /// <summary>CLR type of the <c>ebicsUnsignedRequest</c> envelope binding for this version.</summary>
    public Type UnsignedRequestType { get; }

    /// <summary>CLR type of the <c>ebicsNoPubKeyDigestsRequest</c> envelope binding for this version.</summary>
    public Type NoPubKeyDigestsRequestType { get; }

    /// <summary>CLR type of the <c>ebicsKeyManagementResponse</c> envelope binding for this version.</summary>
    public Type KeyManagementResponseType { get; }
}
