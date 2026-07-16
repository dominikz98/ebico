using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H = EBICO.Core.Schema.H004;

namespace EBICO.Connector.Upload.Envelopes;

/// <summary>
/// The H004 upload envelope builder. H004 submits either a classical upload order type directly (e.g.
/// <c>"CCT"</c>) or the generic <c>"FUL"</c> file upload with a <c>FULOrderParams/FileFormat</c>. The
/// order attribute is <c>DZHNN</c> (order data and ES together, immediate processing — not distributed
/// signing, which would be <c>OZHNN</c>).
/// </summary>
internal sealed class H004UploadEnvelopeBuilder : UploadEnvelopeBuilderBase
{
    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H004;

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildInitRequest(in UploadInitContext ctx)
        => new H.EbicsRequest
        {
            Version = "H004",
            Header = new H.EbicsRequestHeader
            {
                Static = new H.StaticHeaderType
                {
                    HostId = ctx.HostId,
                    PartnerId = ctx.PartnerId,
                    UserId = ctx.UserId,
                    OrderDetails = new H.StaticHeaderOrderDetailsType
                    {
                        OrderType = new H.StaticHeaderOrderDetailsTypeOrderType { Value = ctx.HeaderOrderType },
                        OrderAttribute = H.OrderAttributeType.Dzhnn,
                        OrderParams = ctx.FileFormat is { } fileFormat
                            ? new H.FulOrderParamsType { FileFormat = new H.FileFormatType { Value = fileFormat } }
                            : null,
                    },
                    SecurityMedium = SecurityMedium,
                    NumSegments = ctx.NumSegments,
                },
                Mutable = new H.MutableHeaderType { TransactionPhase = H.TransactionPhaseType.Initialisation },
            },
            Body = new H.EbicsRequestBody
            {
                DataTransfer = new H.DataTransferRequestType
                {
                    DataEncryptionInfo = new H.DataTransferRequestTypeDataEncryptionInfo
                    {
                        EncryptionPubKeyDigest = new H.DataEncryptionInfoTypeEncryptionPubKeyDigest
                        {
                            Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                            Version = ctx.EncryptionVersion,
                            Value = ctx.EncryptionPubKeyDigest,
                        },
                        TransactionKey = ctx.EncryptedTransactionKey,
                    },
                    SignatureData = new H.DataTransferRequestTypeSignatureData { Value = ctx.SignatureData },
                },
            },
        };

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildTransferRequest(in UploadTransferContext ctx)
        => new H.EbicsRequest
        {
            Version = "H004",
            Header = new H.EbicsRequestHeader
            {
                Static = new H.StaticHeaderType { HostId = ctx.HostId, TransactionId = ctx.TransactionId },
                Mutable = new H.MutableHeaderType
                {
                    TransactionPhase = H.TransactionPhaseType.Transfer,
                    SegmentNumber = new H.MutableHeaderTypeSegmentNumber { Value = ctx.SegmentNumber, LastSegment = ctx.LastSegment },
                },
            },
            Body = new H.EbicsRequestBody
            {
                DataTransfer = new H.DataTransferRequestType { OrderData = new H.DataTransferRequestTypeOrderData { Value = ctx.Segment } },
            },
        };

    /// <inheritdoc />
    public override UploadResponseView ParseInitResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsResponse>(responseXml);
        return new UploadResponseView(
            CombineReturnCode(response.Header?.Mutable?.ReturnCode, response.Body?.ReturnCode?.Value),
            response.Header?.Mutable?.ReportText,
            response.Header?.Static?.TransactionId);
    }

    /// <inheritdoc />
    public override UploadResponseView ParseTransferResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsResponse>(responseXml);
        return new UploadResponseView(
            CombineReturnCode(response.Header?.Mutable?.ReturnCode, response.Body?.ReturnCode?.Value),
            response.Header?.Mutable?.ReportText,
            TransactionId: null);
    }
}
