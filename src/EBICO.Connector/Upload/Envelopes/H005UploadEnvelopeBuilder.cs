using EBICO.Core;
using EBICO.Core.Administrative;
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

    // Selects the H005 order params for the initialisation header: the VEU uploads HVE/HVS reference a
    // parked order by id, everything else carries the BTF in BTUOrderParams. The SignatureFlag element
    // is what asks the bank to park the order for the distributed signature (#124).
    private static object? BuildOrderParams(in UploadInitContext ctx)
    {
        if (ctx.Veu is { } veu)
        {
            var service = (veu.Btf ?? ctx.Btf)?.ToRestrictedServiceType();
            return ctx.HeaderOrderType switch
            {
                VeuOrderTypes.AddSignature => new H.HveOrderParamsType
                {
                    PartnerId = veu.PartnerId ?? ctx.PartnerId,
                    Service = service,
                    OrderId = veu.OrderId,
                },
                VeuOrderTypes.CancelOrder => new H.HvsOrderParamsType
                {
                    PartnerId = veu.PartnerId ?? ctx.PartnerId,
                    Service = service,
                    OrderId = veu.OrderId,
                },
                _ => BuildBtuParams(ctx),
            };
        }

        return BuildBtuParams(ctx);
    }

    private static H.BtuParamsType? BuildBtuParams(in UploadInitContext ctx)
    {
        if (ctx.Btf is not { } btf)
        {
            return null;
        }

        return new H.BtuParamsType
        {
            Service = btf.ToRestrictedServiceType(),
            SignatureFlag = ctx.DistributedSignature ? new H.SignatureFlagType() : null,
        };
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
