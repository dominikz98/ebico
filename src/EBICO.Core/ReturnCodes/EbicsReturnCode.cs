namespace EBICO.Core.ReturnCodes;

/// <summary>
/// A six-digit EBICS return code together with its symbolic name and where it belongs in the
/// response (see <see cref="EbicsReturnCodeKind"/>). The static fields form the central EBICS
/// return-code catalogue; <see cref="EbicsReturnCodes"/> is the registry over them.
/// </summary>
/// <remarks>
/// <para>
/// Values and symbolic names follow EBICS Annex 1 ("Return codes"). The codes and their symbolic
/// names are protocol interface constants shared by the server (which reports them) and the
/// connector (which reads them); the human-readable descriptions here are worded in our own terms.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> only the nine codes exercised by the host skeleton and the M3 key
/// management handlers (<see cref="Ok"/>, <see cref="AuthenticationFailed"/>,
/// <see cref="InvalidRequest"/>, <see cref="InternalError"/>, <see cref="InvalidOrderDataFormat"/>,
/// <see cref="InvalidUserOrUserState"/>, <see cref="InvalidOrderType"/>,
/// <see cref="UnsupportedOrderType"/>, <see cref="InvalidXml"/>) are used in the running code today;
/// every other entry is provided for completeness and must be verified against the official EBICS
/// annexes before it is relied upon.
/// </para>
/// </remarks>
/// <param name="Code">The six-digit return code (e.g. <c>"000000"</c>).</param>
/// <param name="SymbolicName">The symbolic EBICS name (e.g. <c>"EBICS_OK"</c>), used as report text.</param>
/// <param name="Kind">Whether the code is reported in the header (technical) or body (business).</param>
public readonly record struct EbicsReturnCode(string Code, string SymbolicName, EbicsReturnCodeKind Kind)
{
    /// <summary>The EBICS return code that denotes success (<c>"000000"</c>).</summary>
    public const string OkCode = "000000";

    // --- Technical codes (reported in header/mutable/ReturnCode) ---

    /// <summary>No error (<c>000000</c>). Also used to fill the unused header/body slot.</summary>
    public static readonly EbicsReturnCode Ok =
        new(OkCode, "EBICS_OK", EbicsReturnCodeKind.Technical);

    /// <summary>
    /// The bank has completed the post-processing of a downloaded order after the receipt (<c>011000</c>).
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode DownloadPostprocessDone =
        new("011000", "EBICS_DOWNLOAD_POSTPROCESS_DONE", EbicsReturnCodeKind.Technical);

    /// <summary>
    /// The bank skipped the post-processing of a downloaded order because the client reported a
    /// negative receipt (<c>011001</c>).
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode DownloadPostprocessSkipped =
        new("011001", "EBICS_DOWNLOAD_POSTPROCESS_SKIPPED", EbicsReturnCodeKind.Technical);

    /// <summary>
    /// A transfer requested fewer segments than were announced during initialisation (<c>011101</c>).
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode TxSegmentNumberUnderrun =
        new("011101", "EBICS_TX_SEGMENT_NUMBER_UNDERRUN", EbicsReturnCodeKind.Technical);

    /// <summary>
    /// The supplied order parameters were ignored by the bank (<c>031001</c>). Informational; the
    /// order is still processed.
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode OrderParamsIgnored =
        new("031001", "EBICS_ORDER_PARAMS_IGNORED", EbicsReturnCodeKind.Technical);

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
    /// The client must recover the interrupted transaction by re-synchronising with the bank (<c>061101</c>).
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode TxRecoverySync =
        new("061101", "EBICS_TX_RECOVERY_SYNC", EbicsReturnCodeKind.Technical);

    // --- Business codes (reported in body/ReturnCode) ---

    /// <summary>
    /// The subscriber is not authorised for the requested order type (<c>090003</c>).
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode AuthorisationOrderTypeFailed =
        new("090003", "EBICS_AUTHORISATION_ORDER_TYPE_FAILED", EbicsReturnCodeKind.Business);

    /// <summary>
    /// The order data is not well-formed / does not conform to the expected format (<c>090004</c>).
    /// For the key-management orders this covers order data that cannot be decompressed/deserialized,
    /// an unusable or unpermitted key version, or key material that cannot be reconstructed.
    /// </summary>
    public static readonly EbicsReturnCode InvalidOrderDataFormat =
        new("090004", "EBICS_INVALID_ORDER_DATA_FORMAT", EbicsReturnCodeKind.Business);

    /// <summary>No downloadable data is available for the requested order (<c>090005</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode NoDownloadDataAvailable =
        new("090005", "EBICS_NO_DOWNLOAD_DATA_AVAILABLE", EbicsReturnCodeKind.Business);

    /// <summary>
    /// The subscriber is unknown or in a state that does not allow the request (<c>091002</c>).
    /// For INI this is the "already initialized" case (the subscriber is no longer <c>New</c>) as
    /// well as an unknown subscriber.
    /// </summary>
    public static readonly EbicsReturnCode InvalidUserOrUserState =
        new("091002", "EBICS_INVALID_USER_OR_USER_STATE", EbicsReturnCodeKind.Business);

    /// <summary>The subscriber is unknown to the bank (<c>091003</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode UserUnknown =
        new("091003", "EBICS_USER_UNKNOWN", EbicsReturnCodeKind.Business);

    /// <summary>The subscriber is known but in a state that does not allow the request (<c>091004</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode InvalidUserState =
        new("091004", "EBICS_INVALID_USER_STATE", EbicsReturnCodeKind.Business);

    /// <summary>The requested order type is invalid / unknown (<c>091005</c>).</summary>
    public static readonly EbicsReturnCode InvalidOrderType =
        new("091005", "EBICS_INVALID_ORDER_TYPE", EbicsReturnCodeKind.Business);

    /// <summary>The requested order type is not supported by this server (<c>091006</c>).</summary>
    public static readonly EbicsReturnCode UnsupportedOrderType =
        new("091006", "EBICS_UNSUPPORTED_ORDER_TYPE", EbicsReturnCodeKind.Business);

    /// <summary>
    /// The bank's public keys must be fetched/updated (HPB) before the request can be processed (<c>091008</c>).
    /// </summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode BankPubkeyUpdateRequired =
        new("091008", "EBICS_BANK_PUBKEY_UPDATE_REQUIRED", EbicsReturnCodeKind.Business);

    /// <summary>A segment exceeds the maximum permitted size (<c>091009</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode SegmentSizeExceeded =
        new("091009", "EBICS_SEGMENT_SIZE_EXCEEDED", EbicsReturnCodeKind.Business);

    /// <summary>The request XML is not well-formed or not schema-valid (<c>091010</c>).</summary>
    public static readonly EbicsReturnCode InvalidXml =
        new("091010", "EBICS_INVALID_XML", EbicsReturnCodeKind.Business);

    /// <summary>The <c>HostID</c> in the request is unknown to this server (<c>091011</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode InvalidHostId =
        new("091011", "EBICS_INVALID_HOST_ID", EbicsReturnCodeKind.Business);

    /// <summary>The transaction ID is unknown to the bank (<c>091101</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode TxUnknownTxid =
        new("091101", "EBICS_TX_UNKNOWN_TXID", EbicsReturnCodeKind.Business);

    /// <summary>The transaction was aborted (<c>091102</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode TxAbort =
        new("091102", "EBICS_TX_ABORT", EbicsReturnCodeKind.Business);

    /// <summary>A message of an already-processed transaction step was replayed (<c>091103</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode TxMessageReplay =
        new("091103", "EBICS_TX_MESSAGE_REPLAY", EbicsReturnCodeKind.Business);

    /// <summary>A transfer requested more segments than were announced during initialisation (<c>091104</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode TxSegmentNumberExceeded =
        new("091104", "EBICS_TX_SEGMENT_NUMBER_EXCEEDED", EbicsReturnCodeKind.Business);

    /// <summary>The request content is not valid for the requested operation (<c>091112</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode InvalidRequestContent =
        new("091112", "EBICS_INVALID_REQUEST_CONTENT", EbicsReturnCodeKind.Business);

    /// <summary>The uploaded order data exceeds the maximum permitted size (<c>091113</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode MaxOrderDataSizeExceeded =
        new("091113", "EBICS_MAX_ORDER_DATA_SIZE_EXCEEDED", EbicsReturnCodeKind.Business);

    /// <summary>The number of segments exceeds the bank's maximum (<c>091114</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode MaxSegmentsExceeded =
        new("091114", "EBICS_MAX_SEGMENTS_EXCEEDED", EbicsReturnCodeKind.Business);

    /// <summary>The number of concurrent transactions exceeds the bank's maximum (<c>091115</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode MaxTransactionsExceeded =
        new("091115", "EBICS_MAX_TRANSACTIONS_EXCEEDED", EbicsReturnCodeKind.Business);

    /// <summary>The <c>PartnerID</c> does not match the one bound to the transaction (<c>091116</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode PartnerIdMismatch =
        new("091116", "EBICS_PARTNER_ID_MISMATCH", EbicsReturnCodeKind.Business);

    /// <summary>The order attribute is incompatible with the requested order type (<c>091117</c>).</summary>
    /// <remarks><b>⚠️ Spec-Vorbehalt:</b> to be verified against EBICS Annex 1.</remarks>
    public static readonly EbicsReturnCode IncompatibleOrderAttribute =
        new("091117", "EBICS_INCOMPATIBLE_ORDER_ATTRIBUTE", EbicsReturnCodeKind.Business);
}
