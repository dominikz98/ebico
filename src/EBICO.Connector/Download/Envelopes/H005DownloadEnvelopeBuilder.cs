using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H = EBICO.Core.Schema.H005;

namespace EBICO.Connector.Download.Envelopes;

/// <summary>
/// The H005 (EBICS 3.0) download envelope builder. Statement/report downloads are requested via
/// <c>AdminOrderType="BTD"</c> and a <c>BTDOrderParams/Service</c> carrying the BTF; administrative
/// downloads (HTD/HKD/HAA/HPD/HAC/PTK) keep their classical <c>AdminOrderType</c> and carry no order
/// params.
/// </summary>
internal sealed class H005DownloadEnvelopeBuilder : DownloadEnvelopeBuilderBase
{
    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildInitRequest(in DownloadInitContext ctx)
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
                            ? new H.BtdParamsType { Service = btf.ToRestrictedServiceType(), DateRange = ToDateRange(ctx.Period) }
                            : null,
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
            Body = new H.EbicsRequestBody(),
        };

    /// <inheritdoc />
    public override IAuthSignedRequestEnvelope BuildReceiptRequest(in DownloadReceiptContext ctx)
        => new H.EbicsRequest
        {
            Version = "H005",
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

    // Maps the version-neutral reporting period onto the H005 BTDOrderParams DateRange element, emitted
    // only when both bounds are present (an open-ended side lets the server apply its default window).
    private static H.DateRangeType? ToDateRange(DateRange? period)
        => period is { Start: { } start, End: { } end }
            ? new H.DateRangeType { Start = start.ToDateTime(TimeOnly.MinValue), End = end.ToDateTime(TimeOnly.MinValue) }
            : null;
}
