using EBICO.Core;
using EBICO.Core.Administrative;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H = EBICO.Core.Schema.H003;

namespace EBICO.Connector.Upload.Envelopes;
/// <summary>
/// The H003 upload envelope builder. H003 submits either a classical upload order type directly (e.g.
/// <c>"CCT"</c>) or the generic <c>"FUL"</c> file upload with a <c>FULOrderParams/FileFormat</c>. The
/// order attribute is <c>DZHNN</c> (order data and ES together, immediate processing — not distributed
/// signing, which would be <c>OZHNN</c>).
/// </summary>
internal sealed class H003UploadEnvelopeBuilder : UploadEnvelopeBuilderBase
{
    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;
    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildInitRequest(in UploadInitContext ctx)
        => new H.EbicsRequest
        {
            Version = "H003",
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
                        OrderAttribute = ctx.DistributedSignature ? H.OrderAttributeType.Ozhnn : H.OrderAttributeType.Dzhnn,
                        OrderParams = BuildOrderParams(ctx),
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
            Version = "H003",
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
    // Selects the order params for the initialisation header: the VEU uploads HVE/HVS reference a parked
    // order by id, a generic FUL upload carries its FileFormat, everything else has none (#124).
    private static object? BuildOrderParams(in UploadInitContext ctx)
    {
        if (ctx.Veu is { } veu)
        {
            switch (ctx.HeaderOrderType)
            {
                case VeuOrderTypes.AddSignature:
                    return new H.HveOrderParamsType
                    {
                        PartnerId = veu.PartnerId ?? ctx.PartnerId,
                        OrderType = veu.OrderType,
                        OrderId = veu.OrderId,
                    };
                case VeuOrderTypes.CancelOrder:
                    return new H.HvsOrderParamsType
                    {
                        PartnerId = veu.PartnerId ?? ctx.PartnerId,
                        OrderType = veu.OrderType,
                        OrderId = veu.OrderId,
                    };
            }
        }

        return ctx.FileFormat is { } fileFormat
            ? new H.FulOrderParamsType { FileFormat = new H.FileFormatType { Value = fileFormat } }
            : null;
    }

    /// <inheritdoc />
    public override UploadResponseView ParseInitResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsResponse>(responseXml);
        return new UploadResponseView(
            CombineOutcome(response.Header?.Mutable?.ReturnCode, response.Header?.Mutable?.ReportText, response.Body?.ReturnCode?.Value),
            response.Header?.Static?.TransactionId);
    }
    /// <inheritdoc />
    public override UploadResponseView ParseTransferResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsResponse>(responseXml);
        return new UploadResponseView(
            CombineOutcome(response.Header?.Mutable?.ReturnCode, response.Header?.Mutable?.ReportText, response.Body?.ReturnCode?.Value),
            transactionId: null);
    }
}
