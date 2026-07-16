using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Upload.Envelopes;

/// <summary>
/// The inputs for an upload <b>initialisation</b> request: the subscriber identifiers, the resolved
/// order identity (the header order/admin-order type plus the H005 BTF or the H003/H004 file format),
/// the announced segment count and the E002 encryption metadata (encrypted transaction key + recipient
/// key digest) together with the electronic-signature (ES) blob.
/// </summary>
/// <param name="HostId">The <c>HostID</c>.</param>
/// <param name="PartnerId">The <c>PartnerID</c>.</param>
/// <param name="UserId">The <c>UserID</c>.</param>
/// <param name="HeaderOrderType">The value placed in the header's <c>OrderType</c> (H003/H004) or <c>AdminOrderType</c> (H005) — e.g. a classical code such as <c>"CCT"</c>, <c>"FUL"</c> or <c>"BTU"</c>.</param>
/// <param name="Btf">The H005 business transaction format placed in <c>BTUOrderParams/Service</c>; ignored for H003/H004.</param>
/// <param name="FileFormat">The H003/H004 <c>FULOrderParams/FileFormat</c> value (only when <paramref name="HeaderOrderType"/> is <c>"FUL"</c>); ignored for H005.</param>
/// <param name="NumSegments">The number of order-data segments the transfer phase will deliver (≥ 1).</param>
/// <param name="EncryptedTransactionKey">The RSA-OAEP-encrypted transaction key (<c>DataEncryptionInfo/TransactionKey</c>).</param>
/// <param name="EncryptionPubKeyDigest">The SHA-256 fingerprint of the bank encryption key (<c>EncryptionPubKeyDigest</c>).</param>
/// <param name="EncryptionVersion">The bank encryption key version code (e.g. <c>"E002"</c>).</param>
/// <param name="SignatureData">The compressed-and-encrypted electronic signature (<c>DataTransfer/SignatureData</c>).</param>
internal readonly record struct UploadInitContext(
    string HostId,
    string PartnerId,
    string UserId,
    string HeaderOrderType,
    BusinessTransactionFormat? Btf,
    string? FileFormat,
    ulong NumSegments,
    byte[] EncryptedTransactionKey,
    byte[] EncryptionPubKeyDigest,
    string EncryptionVersion,
    byte[] SignatureData);

/// <summary>The inputs for a single upload <b>transfer</b> request: the transaction id, the 1-based segment number and the segment bytes.</summary>
/// <param name="HostId">The <c>HostID</c>.</param>
/// <param name="TransactionId">The transaction id assigned by the server in the initialisation response.</param>
/// <param name="SegmentNumber">The 1-based segment number.</param>
/// <param name="LastSegment">Whether this is the last segment of the transaction.</param>
/// <param name="Segment">The raw order-data segment bytes (base64-encoded on the wire by the serializer).</param>
internal readonly record struct UploadTransferContext(
    string HostId,
    byte[] TransactionId,
    ulong SegmentNumber,
    bool LastSegment,
    byte[] Segment);

/// <summary>
/// The version-neutral projection of an upload transaction-step response: the effective return code
/// (the non-OK of the technical header code and the business body code, else EBICS_OK), the header
/// report text and — for an initialisation response — the assigned transaction id.
/// </summary>
/// <param name="ReturnCode">The effective EBICS return code.</param>
/// <param name="ReportText">The human-readable report text from the mutable header, if any.</param>
/// <param name="TransactionId">The assigned transaction id (initialisation response only), or <see langword="null"/>.</param>
internal readonly record struct UploadResponseView(string ReturnCode, string? ReportText, byte[]? TransactionId);

/// <summary>
/// Builds the version-specific upload <c>ebicsRequest</c> envelopes (initialisation and transfer) and
/// projects the corresponding <c>ebicsResponse</c> back into a <see cref="UploadResponseView"/>. One
/// implementation per supported EBICS version, resolved through <see cref="IUploadEnvelopeBuilderRegistry"/>.
/// </summary>
internal interface IUploadEnvelopeBuilder
{
    /// <summary>The EBICS version this builder handles.</summary>
    EbicsVersion Version { get; }

    /// <summary>Builds the unsigned initialisation-phase <c>ebicsRequest</c> (the caller sets <c>AuthSignature</c>).</summary>
    /// <param name="ctx">The initialisation inputs.</param>
    /// <returns>The initialisation envelope, ready for signing.</returns>
    IAuthSignedRequestEnvelope BuildInitRequest(in UploadInitContext ctx);

    /// <summary>Builds the unsigned transfer-phase <c>ebicsRequest</c> for one segment (the caller sets <c>AuthSignature</c>).</summary>
    /// <param name="ctx">The transfer inputs.</param>
    /// <returns>The transfer envelope, ready for signing.</returns>
    IAuthSignedRequestEnvelope BuildTransferRequest(in UploadTransferContext ctx);

    /// <summary>Parses an initialisation-phase <c>ebicsResponse</c> into a <see cref="UploadResponseView"/> (with transaction id).</summary>
    /// <param name="responseXml">The response XML.</param>
    /// <returns>The projected response.</returns>
    UploadResponseView ParseInitResponse(string responseXml);

    /// <summary>Parses a transfer-phase <c>ebicsResponse</c> into a <see cref="UploadResponseView"/>.</summary>
    /// <param name="responseXml">The response XML.</param>
    /// <returns>The projected response.</returns>
    UploadResponseView ParseTransferResponse(string responseXml);
}
