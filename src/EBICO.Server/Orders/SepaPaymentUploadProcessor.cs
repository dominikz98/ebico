using EBICO.Core.Payments;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;
using Microsoft.Extensions.Options;

namespace EBICO.Server.Orders;

/// <summary>
/// The default <see cref="IUploadOrderProcessor"/> for SEPA payment uploads (issue #39): CCT/CIP
/// (<c>pain.001</c>) and CDD/CDB (<c>pain.008</c>). It validates the uploaded pain payload structurally
/// (<see cref="SepaPaymentValidator"/>) and, on success, builds a positive <c>pain.002</c> customer
/// payment status report (<see cref="PainStatusReportBuilder"/>) and files it via the
/// <see cref="IDownloadDataProvider"/> under <see cref="EbicoServerOptions.PaymentStatusReportOrderType"/>
/// for later download. Accept/reject is recorded on the event log.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the validation is structural/semantic, not a full ISO 20022 XSD check, and
/// the electronic signature (ES) over the order data is still not verified (consistent with #32). The
/// end-to-end <em>download</em> of the filed status report (mapping an <c>FDL</c> file format / <c>BTD</c>
/// BTF onto the status-report queue) lands with the download orders (issue #40); today it is observable
/// via the admin API and the provider. See <c>docs/server/payment-orders.md</c> and ADR-0017.
/// </remarks>
public sealed class SepaPaymentUploadProcessor : IUploadOrderProcessor
{
    private readonly IDownloadDataProvider _downloadDataProvider;
    private readonly IEventLog _eventLog;
    private readonly TimeProvider _timeProvider;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the processor.</summary>
    /// <param name="downloadDataProvider">The provider the generated status report is filed into for later download.</param>
    /// <param name="eventLog">The append-only event log the accept/reject outcome is written to.</param>
    /// <param name="timeProvider">The clock used to stamp the status report's creation time.</param>
    /// <param name="options">The server options (the status-report download order type).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SepaPaymentUploadProcessor(
        IDownloadDataProvider downloadDataProvider,
        IEventLog eventLog,
        TimeProvider timeProvider,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(downloadDataProvider);
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _downloadDataProvider = downloadDataProvider;
        _eventLog = eventLog;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    public bool CanProcess(string? effectiveOrderType) => PaymentOrderTypes.IsPaymentOrderType(effectiveOrderType);

    /// <inheritdoc />
    public async Task<UploadOrderResult> ProcessAsync(UploadOrderContext context, CancellationToken ct = default)
    {
        var validation = SepaPaymentValidator.Validate(context.EffectiveOrderType, context.OrderData);

        if (!validation.IsValid)
        {
            await AppendEventAsync(
                context,
                EbicsEventType.OrderRejected,
                EbicsEventSeverity.Warning,
                EbicsReturnCode.InvalidOrderDataFormat,
                $"{context.EffectiveOrderType} payment rejected: {string.Join("; ", validation.Errors)}",
                ct).ConfigureAwait(false);

            return UploadOrderResult.Rejected;
        }

        // Build a positive pain.002 status report echoing the original message identifiers and file it
        // for later download (the "Ablage zur späteren Auslieferung").
        var reportMessageId = "PSR-" + Guid.NewGuid().ToString("N");
        var statusReport = PainStatusReportBuilder.Build(
            validation.MessageId!,
            validation.MessageNameId!,
            reportMessageId,
            _timeProvider.GetUtcNow());

        await _downloadDataProvider
            .EnqueueAsync(context.Subscriber, _options.PaymentStatusReportOrderType, statusReport, ct)
            .ConfigureAwait(false);

        await AppendEventAsync(
            context,
            EbicsEventType.OrderAccepted,
            EbicsEventSeverity.Info,
            EbicsReturnCode.Ok,
            $"{context.EffectiveOrderType} payment accepted (message {validation.MessageId}); "
                + $"pain.002 status report filed under '{_options.PaymentStatusReportOrderType}'.",
            ct).ConfigureAwait(false);

        return UploadOrderResult.Accepted;
    }

    private Task AppendEventAsync(
        UploadOrderContext context,
        EbicsEventType type,
        EbicsEventSeverity severity,
        EbicsReturnCode returnCode,
        string message,
        CancellationToken ct)
        => _eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = type,
                Severity = severity,
                Visibility = EbicsEventVisibility.CustomerVisible,
                HostId = context.Subscriber.HostId,
                PartnerId = context.Subscriber.PartnerId,
                UserId = context.Subscriber.UserId,
                OrderType = context.EffectiveOrderType,
                TransactionId = context.TransactionIdHex,
                ReturnCode = returnCode,
                Message = message,
            },
            ct);
}
