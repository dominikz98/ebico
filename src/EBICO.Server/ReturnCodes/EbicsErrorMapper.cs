using System.Security.Cryptography;
using System.Xml;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Versioning;
using EBICO.Server.Handlers;
using EBICO.Server.State;

namespace EBICO.Server.ReturnCodes;

/// <summary>
/// Default <see cref="IEbicsErrorMapper"/>: the single source of truth mapping the exceptions raised
/// while processing an EBICS request onto EBICS return codes, falling back to
/// <see cref="EbicsReturnCode.InternalError"/> for anything unexpected (a server fault).
/// </summary>
/// <remarks>
/// Order-data faults are surfaced by the handlers as <see cref="EbicsOrderDataException"/> (see
/// <see cref="OrderDataFault"/>), whose dedicated type maps unambiguously to
/// <see cref="EbicsReturnCode.InvalidOrderDataFormat"/>. General-purpose exceptions
/// (<see cref="ArgumentException"/>, a plain <see cref="InvalidOperationException"/>) are deliberately
/// <em>not</em> mapped to a business code — outside the order-data decode step they denote a server
/// bug and must surface as <see cref="EbicsReturnCode.InternalError"/>.
/// </remarks>
public sealed class EbicsErrorMapper : IEbicsErrorMapper
{
    /// <inheritdoc />
    public EbicsReturnCode Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // Invalid order data: undecryptable/undecompressable/undeserializable order data, key
            // material that cannot be reconstructed, or an unusable/unpermitted key version. The
            // handlers wrap these into EbicsOrderDataException; the low-level types are also mapped
            // directly so callers outside a handler (e.g. the transaction engine) map consistently.
            EbicsOrderDataException => EbicsReturnCode.InvalidOrderDataFormat,
            KeyMaterialException => EbicsReturnCode.InvalidOrderDataFormat,
            InvalidKeyVersionException => EbicsReturnCode.InvalidOrderDataFormat,
            KeyVersionNotPermittedException => EbicsReturnCode.InvalidOrderDataFormat,
            InvalidDataException => EbicsReturnCode.InvalidOrderDataFormat,
            FormatException => EbicsReturnCode.InvalidOrderDataFormat,
            CryptographicException => EbicsReturnCode.InvalidOrderDataFormat,

            // Invalid/unknown subscriber, master-data lookup miss, or a rejected lifecycle transition.
            InvalidEbicsIdentifierException => EbicsReturnCode.InvalidUserOrUserState,
            InvalidSubscriberStateTransitionException => EbicsReturnCode.InvalidUserOrUserState,
            MasterDataException => EbicsReturnCode.InvalidUserOrUserState,

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
