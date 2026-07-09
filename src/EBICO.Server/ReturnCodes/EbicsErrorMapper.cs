using System.Xml;
using EBICO.Core.Versioning;

namespace EBICO.Server.ReturnCodes;

/// <summary>
/// Default <see cref="IEbicsErrorMapper"/>: maps the version-dispatch and parsing exceptions
/// raised by <c>EBICO.Core</c> onto EBICS return codes, falling back to
/// <see cref="EbicsReturnCode.InternalError"/> for anything unexpected.
/// </summary>
public sealed class EbicsErrorMapper : IEbicsErrorMapper
{
    /// <inheritdoc />
    public EbicsReturnCode Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // Not well-formed / empty / prohibited DTD / unrecognized root element.
            EbicsEnvelopeFormatException => EbicsReturnCode.InvalidXml,

            // Root namespace belongs to no supported version, or strict version mismatch.
            // ⚠️ Spec-Vorbehalt: ein ebicsResponse hat keinen dedizierten "unsupported version"-Code.
            EbicsVersionNotSupportedException => EbicsReturnCode.InvalidRequest,
            EbicsVersionMismatchException => EbicsReturnCode.InvalidRequest,

            // A well-formed root can still hide a malformed body: DeserializeEnvelope detects the
            // version from the root element but the deeper XmlSerializer.Deserialize then throws an
            // XmlException (wrapped in InvalidOperationException). That is the client's invalid XML,
            // not a server fault.
            XmlException => EbicsReturnCode.InvalidXml,
            InvalidOperationException { InnerException: XmlException } => EbicsReturnCode.InvalidXml,

            _ => EbicsReturnCode.InternalError,
        };
    }
}
