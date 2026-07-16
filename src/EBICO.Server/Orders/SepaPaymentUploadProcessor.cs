using EBICO.Core.Administrative;
using EBICO.Core.Domain;
using EBICO.Core.Payments;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;
using Microsoft.Extensions.Options;

namespace EBICO.Server.Orders;

/// <summary>
/// The default <see cref="IUploadOrderProcessor"/> for SEPA payment uploads (issue #39): CCT/CIP
/// (<c>pain.001</c>) and CDD/CDB (<c>pain.008</c>). It validates the uploaded pain payload structurally
/// (<see cref="SepaPaymentValidator"/>) and then either releases it immediately — building a positive
/// <c>pain.002</c> customer payment status report (<see cref="PainStatusReportBuilder"/>) and filing it via
/// the <see cref="IDownloadDataProvider"/> under <see cref="EbicoServerOptions.PaymentStatusReportOrderType"/>
/// — or, when the upload was submitted for distributed signing (EDS / VEU, issue #42), parks it in the
/// <see cref="IOpenVeuStore"/> to await further signatures. Accept/park/reject is recorded on the event log.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the validation is structural/semantic, not a full ISO 20022 XSD check, and
/// the electronic signature (ES) over the order data is still not verified (consistent with #32). Whether
/// an upload needs distributed signing is taken from the request's signature flag / order attribute rather
/// than from bank-side account signature rules. See <c>docs/server/payment-orders.md</c>,
/// <c>docs/server/veu-orders.md</c> and ADR-0017/0020.
/// </remarks>
public sealed class SepaPaymentUploadProcessor : IUploadOrderProcessor
{
    private readonly IDownloadDataProvider _downloadDataProvider;
    private readonly IOpenVeuStore _veuStore;
    private readonly IMasterDataManager _masterData;
    private readonly IEventLog _eventLog;
    private readonly TimeProvider _timeProvider;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the processor.</summary>
    /// <param name="downloadDataProvider">The provider the generated status report is filed into for later download.</param>
    /// <param name="veuStore">The open-VEU store a distributed-signature upload is parked in (issue #42).</param>
    /// <param name="masterData">The master-data manager used to resolve the submitting subscriber (name / signature class).</param>
    /// <param name="eventLog">The append-only event log the accept/park/reject outcome is written to.</param>
    /// <param name="timeProvider">The clock used to stamp the status report and the parked order.</param>
    /// <param name="options">The server options (status-report order type, required signatures).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SepaPaymentUploadProcessor(
        IDownloadDataProvider downloadDataProvider,
        IOpenVeuStore veuStore,
        IMasterDataManager masterData,
        IEventLog eventLog,
        TimeProvider timeProvider,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(downloadDataProvider);
        ArgumentNullException.ThrowIfNull(veuStore);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _downloadDataProvider = downloadDataProvider;
        _veuStore = veuStore;
        _masterData = masterData;
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

        // A distributed-signature submission (EDS / VEU, issue #42) is parked to await further signatures —
        // unless the submitter's own signature already satisfies the required count, in which case it is
        // released immediately like an ordinary upload.
        if (context.DistributedSignature)
        {
            var parked = await TryParkForDistributedSigningAsync(context, ct).ConfigureAwait(false);
            if (parked)
            {
                return UploadOrderResult.Accepted;
            }
        }

        await FileStatusReportAsync(context, validation, ct).ConfigureAwait(false);
        return UploadOrderResult.Accepted;
    }

    // Parks the validated order in the open-VEU store when it still needs further signatures. Returns false
    // when the submitter's own bank-technical signature already meets the required count (the caller then
    // releases it immediately).
    private async Task<bool> TryParkForDistributedSigningAsync(UploadOrderContext context, CancellationToken ct)
    {
        var subscriber = await _masterData
            .GetSubscriberAsync(context.Subscriber.HostId, context.Subscriber.PartnerId, context.Subscriber.UserId, ct)
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        var originator = new VeuSignerView(
            context.Subscriber.PartnerId.Value,
            context.Subscriber.UserId.Value,
            subscriber?.Name,
            now,
            Permission: null);

        // The submitter contributes their own bank-technical signature (if any) as the first signature.
        var submitterClass = subscriber is null ? null : BankTechnicalClass(subscriber, context.EffectiveOrderType);
        var initialSigners = submitterClass is { } sig
            ? new[] { originator with { Permission = sig } }
            : [];

        if (initialSigners.Length >= _options.VeuRequiredSignatures)
        {
            return false;
        }

        var order = new OpenVeuOrder(
            context.Subscriber.HostId,
            context.Subscriber.PartnerId,
            context.Version,
            context.EffectiveOrderType,
            context.OrderData,
            originator,
            _options.VeuRequiredSignatures,
            now,
            initialSigners);

        await _veuStore.AddAsync(order, ct).ConfigureAwait(false);

        await AppendEventAsync(
            context,
            EbicsEventType.VeuPending,
            EbicsEventSeverity.Info,
            EbicsReturnCode.Ok,
            $"{context.EffectiveOrderType} payment parked for distributed signing as order {order.OrderId} "
                + $"({order.NumSigDone}/{order.NumSigRequired} signatures).",
            ct).ConfigureAwait(false);

        return true;
    }

    // Builds a positive pain.002 status report and files it for later download, recording the acceptance.
    private async Task FileStatusReportAsync(UploadOrderContext context, PainValidationResult validation, CancellationToken ct)
    {
        await PaymentStatusReportFiling
            .FileAsync(
                _downloadDataProvider,
                _options.PaymentStatusReportOrderType,
                context.Subscriber,
                validation.MessageId!,
                validation.MessageNameId!,
                _timeProvider.GetUtcNow(),
                ct)
            .ConfigureAwait(false);

        await AppendEventAsync(
            context,
            EbicsEventType.OrderAccepted,
            EbicsEventSeverity.Info,
            EbicsReturnCode.Ok,
            $"{context.EffectiveOrderType} payment accepted (message {validation.MessageId}); "
                + $"pain.002 status report filed under '{_options.PaymentStatusReportOrderType}'.",
            ct).ConfigureAwait(false);
    }

    // The submitter's first bank-technical (E/A/B) signature class for the order type, or null when the
    // subscriber holds only a transport permission (they carry the data but contribute no signature).
    private static SignatureClass? BankTechnicalClass(Subscriber subscriber, string orderType)
        => subscriber.Permissions
            .Where(p => string.Equals(p.OrderType, orderType, StringComparison.Ordinal) && p.SignatureClass.IsBankTechnical())
            .Select(p => (SignatureClass?)p.SignatureClass)
            .FirstOrDefault();

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
