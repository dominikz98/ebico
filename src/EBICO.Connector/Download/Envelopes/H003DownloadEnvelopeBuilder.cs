using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H = EBICO.Core.Schema.H003;

namespace EBICO.Connector.Download.Envelopes;

/// <summary>
/// The H003 download envelope builder. Downloads are requested either via the generic <c>"FDL"</c>
/// file download with a <c>FDLOrderParams/FileFormat</c>, or via a classical download order type
/// submitted directly (e.g. <c>"STA"</c>, <c>"HTD"</c>) with an optional <c>StandardOrderParams</c>.
/// The order attribute is <c>DZHNN</c> (as for the corresponding uploads).
/// </summary>
internal sealed class H003DownloadEnvelopeBuilder : DownloadEnvelopeBuilderBase
{
    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildInitRequest(in DownloadInitContext ctx)
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
                        OrderAttribute = H.OrderAttributeType.Dzhnn,
                        OrderParams = BuildOrderParams(ctx),
                    },
                    SecurityMedium = SecurityMedium,
                },
                Mutable = new H.MutableHeaderType { TransactionPhase = H.TransactionPhaseType.Initialisation },
            },
            Body = new H.EbicsRequestBody(),
        };

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildTransferRequest(in DownloadTransferContext ctx)
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
            Body = new H.EbicsRequestBody(),
        };

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildReceiptRequest(in DownloadReceiptContext ctx)
        => new H.EbicsRequest
        {
            Version = "H003",
            Header = new H.EbicsRequestHeader
            {
                Static = new H.StaticHeaderType { HostId = ctx.HostId, TransactionId = ctx.TransactionId },
                Mutable = new H.MutableHeaderType { TransactionPhase = H.TransactionPhaseType.Receipt },
            },
            Body = new H.EbicsRequestBody
            {
                TransferReceipt = new H.EbicsRequestBodyTransferReceipt { ReceiptCode = ctx.ReceiptCode },
            },
        };

    /// <inheritdoc />
    public override DownloadInitResponseView ParseInitResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsResponse>(responseXml);
        var dataTransfer = response.Body?.DataTransfer;
        return new DownloadInitResponseView(
            CombineReturnCode(response.Header?.Mutable?.ReturnCode, response.Body?.ReturnCode?.Value),
            response.Header?.Mutable?.ReportText,
            response.Header?.Static?.TransactionId,
            response.Header?.Static?.NumSegments,
            dataTransfer?.OrderData?.Value,
            dataTransfer?.DataEncryptionInfo?.TransactionKey);
    }

    /// <inheritdoc />
    public override DownloadTransferResponseView ParseTransferResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsResponse>(responseXml);
        return new DownloadTransferResponseView(
            CombineReturnCode(response.Header?.Mutable?.ReturnCode, response.Body?.ReturnCode?.Value),
            response.Header?.Mutable?.ReportText,
            response.Body?.DataTransfer?.OrderData?.Value);
    }

    /// <inheritdoc />
    public override DownloadReceiptResponseView ParseReceiptResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsResponse>(responseXml);
        return new DownloadReceiptResponseView(
            CombineReturnCode(response.Header?.Mutable?.ReturnCode, response.Body?.ReturnCode?.Value),
            response.Header?.Mutable?.ReportText);
    }

    // FDL → FDLOrderParams (FileFormat + optional DateRange); a classical order type → StandardOrderParams
    // (only when a period is present, otherwise no params element).
    private static object? BuildOrderParams(in DownloadInitContext ctx)
    {
        if (ctx.FileFormat is { } fileFormat)
        {
            return new H.FdlOrderParamsType
            {
                FileFormat = new H.FileFormatType { Value = fileFormat },
                DateRange = ctx.Period is { Start: { } fs, End: { } fe }
                    ? new H.FdlOrderParamsTypeDateRange { Start = fs.ToDateTime(TimeOnly.MinValue), End = fe.ToDateTime(TimeOnly.MinValue) }
                    : null,
            };
        }

        return ctx.Period is { Start: { } start, End: { } end }
            ? new H.StandardOrderParamsType
            {
                DateRange = new H.StandardOrderParamsTypeDateRange { Start = start.ToDateTime(TimeOnly.MinValue), End = end.ToDateTime(TimeOnly.MinValue) },
            }
            : null;
    }
}
