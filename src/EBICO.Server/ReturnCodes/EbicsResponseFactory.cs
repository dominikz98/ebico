using EBICO.Core;
using EBICO.Core.Versioning;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Server.ReturnCodes;

/// <summary>
/// Builds minimal, well-formed <c>ebicsResponse</c> envelopes carrying a single
/// <see cref="EbicsReturnCode"/>, using the committed per-version schema bindings.
/// </summary>
/// <remarks>
/// A technical code lands in <c>header/mutable/ReturnCode</c>, a business code in
/// <c>body/ReturnCode</c>; the respective other slot is filled with <see cref="EbicsReturnCode.OkCode"/>.
/// <b>⚠️ Spec-Vorbehalt:</b> the response is <em>not</em> signed (AuthSignature) — the response
/// authentication signature (X002) is M4. Strict clients may reject unsigned responses. The
/// exact header/body placement and the mandatory-but-empty static header are still to be verified
/// against the official EBICS annexes.
/// </remarks>
public sealed class EbicsResponseFactory
{
    /// <summary>
    /// Builds an <c>ebicsResponse</c> for <paramref name="version"/> reporting
    /// <paramref name="returnCode"/>.
    /// </summary>
    /// <param name="version">The protocol version whose bindings/namespace to use.</param>
    /// <param name="returnCode">The return code to report.</param>
    /// <returns>The response envelope, ready for serialization.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public IEbicsResponseEnvelope BuildErrorResponse(EbicsVersion version, EbicsReturnCode returnCode)
    {
        var headerCode = returnCode.Kind == EbicsReturnCodeKind.Technical ? returnCode.Code : EbicsReturnCode.OkCode;
        var bodyCode = returnCode.Kind == EbicsReturnCodeKind.Business ? returnCode.Code : EbicsReturnCode.OkCode;

        // ReportText interprets the *header* return code, so it stays consistent with it: for a
        // business code the header reports EBICS_OK (the message exchange succeeded; the order-level
        // result is in body/ReturnCode). The body return code has no text slot in the schema.
        var reportText = returnCode.Kind == EbicsReturnCodeKind.Technical
            ? returnCode.SymbolicName
            : EbicsReturnCode.Ok.SymbolicName;

        return version switch
        {
            EbicsVersion.H003 => new H003.EbicsResponse
            {
                Version = "H003",
                Header = new H003.EbicsResponseHeader
                {
                    Static = new H003.ResponseStaticHeaderType(),
                    Mutable = new H003.ResponseMutableHeaderType
                    {
                        TransactionPhase = H003.TransactionPhaseType.Initialisation,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H003.EbicsResponseBody
                {
                    ReturnCode = new H003.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H004 => new H004.EbicsResponse
            {
                Version = "H004",
                Header = new H004.EbicsResponseHeader
                {
                    Static = new H004.ResponseStaticHeaderType(),
                    Mutable = new H004.ResponseMutableHeaderType
                    {
                        TransactionPhase = H004.TransactionPhaseType.Initialisation,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H004.EbicsResponseBody
                {
                    ReturnCode = new H004.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            EbicsVersion.H005 => new H005.EbicsResponse
            {
                Version = "H005",
                Header = new H005.EbicsResponseHeader
                {
                    Static = new H005.ResponseStaticHeaderType(),
                    Mutable = new H005.ResponseMutableHeaderType
                    {
                        TransactionPhase = H005.TransactionPhaseType.Initialisation,
                        ReturnCode = headerCode,
                        ReportText = reportText,
                    },
                },
                Body = new H005.EbicsResponseBody
                {
                    ReturnCode = new H005.EbicsResponseBodyReturnCode { Value = bodyCode },
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }
}
