using EBICO.Core.Administrative;
using EBICO.Core.Domain;
using EBICO.Core.Payments;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;
using Microsoft.Extensions.Options;

namespace EBICO.Server.Orders;

/// <summary>
/// The <see cref="IUploadOrderProcessor"/> for the distributed-electronic-signature upload orders HVE (add a
/// signature) and HVS (cancel/reject an order) — issue #42. Both reference an order held in the
/// <see cref="IOpenVeuStore"/> by its order id. HVE adds the signing subscriber's bank-technical signature
/// and, once the required number of signatures is reached, releases the order (files the positive
/// <c>pain.002</c> and removes it from the store); HVS removes the order. Every action is recorded on the
/// event log.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the electronic signature carried by an HVE upload is not verified; "signing"
/// records that an authorised subscriber (one holding a bank-technical E/A/B permission for the underlying
/// order type) submitted an HVE. The duplicate-signature and already-complete rejections reuse
/// <see cref="EbicsReturnCode.InvalidOrderDataFormat"/> as the emulator has no dedicated code for them; an
/// unknown order id maps to <see cref="EbicsReturnCode.InvalidOrderIdentifier"/>. See
/// <c>docs/server/veu-orders.md</c> and ADR-0020.
/// </remarks>
public sealed class VeuSignatureUploadProcessor : IUploadOrderProcessor
{
    private readonly IOpenVeuStore _veuStore;
    private readonly IMasterDataManager _masterData;
    private readonly IDownloadDataProvider _downloadDataProvider;
    private readonly IEventLog _eventLog;
    private readonly TimeProvider _timeProvider;
    private readonly EbicoServerOptions _options;

    /// <summary>Initializes the processor.</summary>
    /// <param name="veuStore">The open-VEU store holding the orders awaiting signatures.</param>
    /// <param name="masterData">The master-data manager used to resolve the signing subscriber (name / authorisation).</param>
    /// <param name="downloadDataProvider">The provider the released order's <c>pain.002</c> status report is filed into.</param>
    /// <param name="eventLog">The append-only event log the VEU actions are written to.</param>
    /// <param name="timeProvider">The clock used to stamp signatures and the status report.</param>
    /// <param name="options">The server options (status-report order type).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public VeuSignatureUploadProcessor(
        IOpenVeuStore veuStore,
        IMasterDataManager masterData,
        IDownloadDataProvider downloadDataProvider,
        IEventLog eventLog,
        TimeProvider timeProvider,
        IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(veuStore);
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(downloadDataProvider);
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _veuStore = veuStore;
        _masterData = masterData;
        _downloadDataProvider = downloadDataProvider;
        _eventLog = eventLog;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    public bool CanProcess(string? effectiveOrderType) => VeuOrderTypes.IsVeuUploadOrderType(effectiveOrderType);

    /// <inheritdoc />
    public async Task<UploadOrderResult> ProcessAsync(UploadOrderContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(context.OrderId))
        {
            return new UploadOrderResult(EbicsReturnCode.InvalidOrderIdentifier);
        }

        var subscriberRef = context.Subscriber;
        var order = await _veuStore
            .TryGetAsync(subscriberRef.HostId, subscriberRef.PartnerId, context.OrderId, ct)
            .ConfigureAwait(false);
        if (order is null)
        {
            return new UploadOrderResult(EbicsReturnCode.InvalidOrderIdentifier);
        }

        return context.EffectiveOrderType == VeuOrderTypes.CancelOrder
            ? await CancelAsync(context, order, ct).ConfigureAwait(false)
            : await SignAsync(context, order, ct).ConfigureAwait(false);
    }

    private async Task<UploadOrderResult> SignAsync(UploadOrderContext context, OpenVeuOrder order, CancellationToken ct)
    {
        var subscriberRef = context.Subscriber;
        var signer = await _masterData
            .GetSubscriberAsync(subscriberRef.HostId, subscriberRef.PartnerId, subscriberRef.UserId, ct)
            .ConfigureAwait(false);
        if (signer is null)
        {
            return new UploadOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // The signer must be authorised to sign the underlying order (hold an E/A/B permission for it).
        var signerClass = BankTechnicalClass(signer, order.EffectiveOrderType);
        if (signerClass is not { } permission)
        {
            return new UploadOrderResult(EbicsReturnCode.AuthorisationOrderTypeFailed);
        }

        var now = _timeProvider.GetUtcNow();
        var signerView = new VeuSignerView(
            subscriberRef.PartnerId.Value,
            subscriberRef.UserId.Value,
            signer.Name,
            now,
            permission);

        var outcome = await _veuStore
            .TrySignAsync(subscriberRef.HostId, subscriberRef.PartnerId, order.OrderId, signerView, ct)
            .ConfigureAwait(false);

        switch (outcome.Status)
        {
            case VeuSignStatus.NotFound:
                return new UploadOrderResult(EbicsReturnCode.InvalidOrderIdentifier);
            case VeuSignStatus.DuplicateSigner:
            case VeuSignStatus.AlreadyComplete:
                return UploadOrderResult.Rejected;
        }

        var signed = outcome.Order!;
        if (signed.IsFullySigned)
        {
            await ReleaseAsync(context, signed, ct).ConfigureAwait(false);
        }
        else
        {
            await AppendEventAsync(
                context,
                EbicsEventType.VeuSigned,
                EbicsEventSeverity.Info,
                EbicsReturnCode.Ok,
                $"HVE signature added to order {signed.OrderId} ({signed.NumSigDone}/{signed.NumSigRequired} signatures).",
                ct).ConfigureAwait(false);
        }

        return UploadOrderResult.Accepted;
    }

    // Releases a fully signed order: files the positive pain.002 for the originator and removes it.
    private async Task ReleaseAsync(UploadOrderContext context, OpenVeuOrder order, CancellationToken ct)
    {
        var validation = SepaPaymentValidator.Validate(order.EffectiveOrderType, order.OrderData);
        if (validation.IsValid)
        {
            var originator = new SubscriberKeyRef(
                order.HostId,
                order.PartnerId,
                UserId.Create(order.Originator.UserId));

            await PaymentStatusReportFiling
                .FileAsync(
                    _downloadDataProvider,
                    _options.PaymentStatusReportOrderType,
                    originator,
                    validation.MessageId!,
                    validation.MessageNameId!,
                    _timeProvider.GetUtcNow(),
                    ct)
                .ConfigureAwait(false);
        }

        await _veuStore.RemoveAsync(order.HostId, order.PartnerId, order.OrderId, ct).ConfigureAwait(false);

        await AppendEventAsync(
            context,
            EbicsEventType.VeuReleased,
            EbicsEventSeverity.Info,
            EbicsReturnCode.Ok,
            $"Order {order.OrderId} ({order.EffectiveOrderType}) released after {order.NumSigDone} signature(s); "
                + $"pain.002 status report filed under '{_options.PaymentStatusReportOrderType}'.",
            ct).ConfigureAwait(false);

        await AppendEventAsync(
            context,
            EbicsEventType.OrderAccepted,
            EbicsEventSeverity.Info,
            EbicsReturnCode.Ok,
            $"{order.EffectiveOrderType} payment accepted (order {order.OrderId}).",
            ct).ConfigureAwait(false);
    }

    private async Task<UploadOrderResult> CancelAsync(UploadOrderContext context, OpenVeuOrder order, CancellationToken ct)
    {
        var subscriberRef = context.Subscriber;

        // A cancellation (HVS) is allowed for the originator or a subscriber authorised to sign the order.
        var isOriginator = string.Equals(order.Originator.UserId, subscriberRef.UserId.Value, StringComparison.Ordinal)
            && string.Equals(order.Originator.PartnerId, subscriberRef.PartnerId.Value, StringComparison.Ordinal);
        if (!isOriginator)
        {
            var subscriber = await _masterData
                .GetSubscriberAsync(subscriberRef.HostId, subscriberRef.PartnerId, subscriberRef.UserId, ct)
                .ConfigureAwait(false);
            if (subscriber is null || !subscriber.CanAuthorize(order.EffectiveOrderType))
            {
                return new UploadOrderResult(EbicsReturnCode.AuthorisationOrderTypeFailed);
            }
        }

        await _veuStore.RemoveAsync(subscriberRef.HostId, subscriberRef.PartnerId, order.OrderId, ct).ConfigureAwait(false);

        await AppendEventAsync(
            context,
            EbicsEventType.VeuCancelled,
            EbicsEventSeverity.Warning,
            EbicsReturnCode.Ok,
            $"Order {order.OrderId} ({order.EffectiveOrderType}) cancelled via HVS.",
            ct).ConfigureAwait(false);

        return UploadOrderResult.Accepted;
    }

    // The subscriber's first bank-technical (E/A/B) signature class for the order type, or null when they
    // hold no authorising permission for it.
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
