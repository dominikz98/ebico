using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Download.Envelopes;

/// <summary>
/// The inputs for a download <b>initialisation</b> request: the subscriber identifiers, the resolved
/// order identity (the header order/admin-order type plus the H005 BTF or the H003/H004 file format)
/// and an optional reporting period.
/// </summary>
/// <param name="HostId">The <c>HostID</c>.</param>
/// <param name="PartnerId">The <c>PartnerID</c>.</param>
/// <param name="UserId">The <c>UserID</c>.</param>
/// <param name="HeaderOrderType">The value placed in the header's <c>OrderType</c> (H003/H004) or <c>AdminOrderType</c> (H005) — e.g. a classical code such as <c>"STA"</c>, <c>"FDL"</c>, <c>"BTD"</c> or an administrative code such as <c>"HTD"</c>.</param>
/// <param name="Btf">The H005 business transaction format placed in <c>BTDOrderParams/Service</c> (statement/report downloads); <see langword="null"/> for H003/H004 and for H005 administrative downloads.</param>
/// <param name="FileFormat">The H003/H004 <c>FDLOrderParams/FileFormat</c> value (only when <paramref name="HeaderOrderType"/> is <c>"FDL"</c>); ignored otherwise.</param>
/// <param name="Period">An optional closed reporting period placed in the version-specific order params; emitted only when both bounds are set.</param>
internal readonly record struct DownloadInitContext(
    string HostId,
    string PartnerId,
    string UserId,
    string HeaderOrderType,
    BusinessTransactionFormat? Btf,
    string? FileFormat,
    DateRange? Period);

/// <summary>The inputs for a single download <b>transfer</b> request: the transaction id and the 1-based segment number to fetch.</summary>
/// <param name="HostId">The <c>HostID</c>.</param>
/// <param name="TransactionId">The transaction id assigned by the server in the initialisation response.</param>
/// <param name="SegmentNumber">The 1-based number of the segment being requested.</param>
/// <param name="LastSegment">Whether this is the last segment of the transaction.</param>
internal readonly record struct DownloadTransferContext(
    string HostId,
    byte[] TransactionId,
    ulong SegmentNumber,
    bool LastSegment);

/// <summary>The inputs for a download <b>receipt</b> request: the transaction id and the receipt code (0 = positive, 1 = negative).</summary>
/// <param name="HostId">The <c>HostID</c>.</param>
/// <param name="TransactionId">The transaction id assigned by the server in the initialisation response.</param>
/// <param name="ReceiptCode">The receipt code: <c>0</c> acknowledges a successful download, <c>1</c> reports a post-processing failure (the server re-provides the data).</param>
internal readonly record struct DownloadReceiptContext(
    string HostId,
    byte[] TransactionId,
    byte ReceiptCode);

/// <summary>
/// The version-neutral projection of a download <b>initialisation</b> response: the effective return
/// code, the header report text, the assigned transaction id, the announced segment count, the first
/// order-data segment and the E002 <c>DataEncryptionInfo/TransactionKey</c> that decrypts the whole
/// transaction.
/// </summary>
/// <param name="ReturnCode">The effective EBICS return code.</param>
/// <param name="ReportText">The human-readable report text from the mutable header, if any.</param>
/// <param name="TransactionId">The assigned transaction id, or <see langword="null"/>.</param>
/// <param name="NumSegments">The announced number of order-data segments, or <see langword="null"/>.</param>
/// <param name="Segment">The first order-data segment (ciphertext), or <see langword="null"/>.</param>
/// <param name="EncryptedTransactionKey">The RSA-OAEP-encrypted transaction key, or <see langword="null"/>.</param>
internal readonly record struct DownloadInitResponseView(
    string ReturnCode,
    string? ReportText,
    byte[]? TransactionId,
    ulong? NumSegments,
    byte[]? Segment,
    byte[]? EncryptedTransactionKey);

/// <summary>The version-neutral projection of a download <b>transfer</b> response: the effective return code, the report text and the delivered order-data segment.</summary>
/// <param name="ReturnCode">The effective EBICS return code.</param>
/// <param name="ReportText">The human-readable report text from the mutable header, if any.</param>
/// <param name="Segment">The delivered order-data segment (ciphertext), or <see langword="null"/>.</param>
internal readonly record struct DownloadTransferResponseView(string ReturnCode, string? ReportText, byte[]? Segment);

/// <summary>The version-neutral projection of a download <b>receipt</b> response: the effective return code and the report text.</summary>
/// <param name="ReturnCode">The effective EBICS return code (e.g. <c>011000</c> on a positive receipt).</param>
/// <param name="ReportText">The human-readable report text from the mutable header, if any.</param>
internal readonly record struct DownloadReceiptResponseView(string ReturnCode, string? ReportText);

/// <summary>
/// Builds the version-specific download <c>ebicsRequest</c> envelopes (initialisation, transfer and
/// receipt) and projects the corresponding <c>ebicsResponse</c> back into the view records. One
/// implementation per supported EBICS version, resolved through <see cref="IDownloadEnvelopeBuilderRegistry"/>.
/// </summary>
internal interface IDownloadEnvelopeBuilder
{
    /// <summary>The EBICS version this builder handles.</summary>
    EbicsVersion Version { get; }

    /// <summary>Builds the unsigned initialisation-phase <c>ebicsRequest</c> (the caller sets <c>AuthSignature</c>).</summary>
    /// <param name="ctx">The initialisation inputs.</param>
    /// <returns>The initialisation envelope, ready for signing.</returns>
    IAuthSignedRequestEnvelope BuildInitRequest(in DownloadInitContext ctx);

    /// <summary>Builds the unsigned transfer-phase <c>ebicsRequest</c> requesting one segment (the caller sets <c>AuthSignature</c>).</summary>
    /// <param name="ctx">The transfer inputs.</param>
    /// <returns>The transfer envelope, ready for signing.</returns>
    IAuthSignedRequestEnvelope BuildTransferRequest(in DownloadTransferContext ctx);

    /// <summary>Builds the unsigned receipt-phase <c>ebicsRequest</c> (the caller sets <c>AuthSignature</c>).</summary>
    /// <param name="ctx">The receipt inputs.</param>
    /// <returns>The receipt envelope, ready for signing.</returns>
    IAuthSignedRequestEnvelope BuildReceiptRequest(in DownloadReceiptContext ctx);

    /// <summary>Parses an initialisation-phase <c>ebicsResponse</c> into a <see cref="DownloadInitResponseView"/>.</summary>
    /// <param name="responseXml">The response XML.</param>
    /// <returns>The projected response.</returns>
    DownloadInitResponseView ParseInitResponse(string responseXml);

    /// <summary>Parses a transfer-phase <c>ebicsResponse</c> into a <see cref="DownloadTransferResponseView"/>.</summary>
    /// <param name="responseXml">The response XML.</param>
    /// <returns>The projected response.</returns>
    DownloadTransferResponseView ParseTransferResponse(string responseXml);

    /// <summary>Parses a receipt-phase <c>ebicsResponse</c> into a <see cref="DownloadReceiptResponseView"/>.</summary>
    /// <param name="responseXml">The response XML.</param>
    /// <returns>The projected response.</returns>
    DownloadReceiptResponseView ParseReceiptResponse(string responseXml);
}
