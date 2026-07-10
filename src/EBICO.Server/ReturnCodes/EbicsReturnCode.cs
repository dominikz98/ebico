namespace EBICO.Server.ReturnCodes;

/// <summary>
/// A six-digit EBICS return code together with its symbolic name and where it belongs in the
/// response (see <see cref="EbicsReturnCodeKind"/>).
/// </summary>
/// <remarks>
/// <b>Preliminary, server-local catalogue.</b> Only the codes the host skeleton (#25) needs are
/// defined here; the full, central EBICS return-code catalogue is defined in issue #36 (M4) — this
/// mirrors the deliberately preliminary <c>EBICO.Connector.EbicsResult</c>. Values follow EBICS
/// Annex 1; entries flagged in the plan as <c>⚠️ Spec-Vorbehalt</c> still need verification against
/// the official annexes.
/// </remarks>
/// <param name="Code">The six-digit return code (e.g. <c>"000000"</c>).</param>
/// <param name="SymbolicName">The symbolic EBICS name (e.g. <c>"EBICS_OK"</c>), used as report text.</param>
/// <param name="Kind">Whether the code is reported in the header (technical) or body (business).</param>
public readonly record struct EbicsReturnCode(string Code, string SymbolicName, EbicsReturnCodeKind Kind)
{
    /// <summary>The EBICS return code that denotes success (<c>"000000"</c>).</summary>
    public const string OkCode = "000000";

    /// <summary>No error (<c>000000</c>). Also used to fill the unused header/body slot.</summary>
    public static readonly EbicsReturnCode Ok =
        new(OkCode, "EBICS_OK", EbicsReturnCodeKind.Technical);

    /// <summary>Authentication signature verification failed (<c>061001</c>).</summary>
    public static readonly EbicsReturnCode AuthenticationFailed =
        new("061001", "EBICS_AUTHENTICATION_FAILED", EbicsReturnCodeKind.Technical);

    /// <summary>The request does not conform to the EBICS specification (<c>061002</c>).</summary>
    public static readonly EbicsReturnCode InvalidRequest =
        new("061002", "EBICS_INVALID_REQUEST", EbicsReturnCodeKind.Technical);

    /// <summary>An internal server error occurred (<c>061099</c>).</summary>
    public static readonly EbicsReturnCode InternalError =
        new("061099", "EBICS_INTERNAL_ERROR", EbicsReturnCodeKind.Technical);

    /// <summary>
    /// The order data is not well-formed / does not conform to the expected format (<c>090004</c>).
    /// For INI this covers order data that cannot be decompressed/deserialized, an unusable or
    /// unpermitted signature key version, or key material that cannot be reconstructed.
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> the exact code is to be verified against EBICS Annex 1 (full catalogue: #36/M4).</remarks>
    public static readonly EbicsReturnCode InvalidOrderDataFormat =
        new("090004", "EBICS_INVALID_ORDER_DATA_FORMAT", EbicsReturnCodeKind.Business);

    /// <summary>
    /// The subscriber is unknown or in a state that does not allow the request (<c>091002</c>).
    /// For INI this is the "already initialized" case (the subscriber is no longer <c>New</c>) as
    /// well as an unknown subscriber.
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> the exact code is to be verified against EBICS Annex 1 (full catalogue: #36/M4).</remarks>
    public static readonly EbicsReturnCode InvalidUserOrUserState =
        new("091002", "EBICS_INVALID_USER_OR_USER_STATE", EbicsReturnCodeKind.Business);

    /// <summary>The requested order type is invalid / unknown (<c>091005</c>).</summary>
    public static readonly EbicsReturnCode InvalidOrderType =
        new("091005", "EBICS_INVALID_ORDER_TYPE", EbicsReturnCodeKind.Business);

    /// <summary>The requested order type is not supported by this server (<c>091006</c>).</summary>
    public static readonly EbicsReturnCode UnsupportedOrderType =
        new("091006", "EBICS_UNSUPPORTED_ORDER_TYPE", EbicsReturnCodeKind.Business);

    /// <summary>The request XML is not well-formed or not schema-valid (<c>091010</c>).</summary>
    public static readonly EbicsReturnCode InvalidXml =
        new("091010", "EBICS_INVALID_XML", EbicsReturnCodeKind.Business);
}
