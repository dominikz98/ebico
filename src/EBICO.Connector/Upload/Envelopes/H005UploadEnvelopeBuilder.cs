using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H = EBICO.Core.Schema.H005;

namespace EBICO.Connector.Upload.Envelopes;

/// <summary>
/// The H005 (EBICS 3.0) upload envelope builder. H005 submits the business transaction via
/// <c>AdminOrderType="BTU"</c> and a <c>BTUOrderParams/Service</c> carrying the BTF.
/// </summary>
internal sealed class H005UploadEnvelopeBuilder : UploadEnvelopeBuilderBase
{
    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildInitRequest(in UploadInitContext ctx)
        => new H.EbicsRequest
        {
            Version = "H005",
            Header = new H.EbicsRequestHeader
            {
                Static = new H.StaticHeaderType
                {
                    HostId = ctx.HostId,
                    PartnerId = ctx.PartnerId,
                    UserId = ctx.UserId,
                    OrderDetails = new H.StaticHeaderOrderDetailsType
                    {
                        AdminOrderType = new H.StaticHeaderOrderDetailsTypeAdminOrderType { Value = ctx.HeaderOrderType },
                        OrderParams = ctx.Btf is { } btf
                            ? new H.BtuParamsType { Service = btf.ToRestrictedServiceType() }
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
            Version = "H005",
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
