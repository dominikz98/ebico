using System.Globalization;
using EBICO.Core.Administrative;
using EBICO.Core.Btf;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;

namespace EBICO.Server.Orders;

/// <summary>
/// The <see cref="IDownloadOrderProcessor"/> for the master-data / parameter download orders HTD, HKD, HAA
/// and HPD (issue #41): it reads the requesting subscriber's bank/partner/subscriber master data from the
/// <see cref="IMasterDataManager"/> and renders the matching response order data via
/// <see cref="SubscriberInfoContentBuilder"/>. The generation outcome is recorded on the event log.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the content reflects the seeded master data only; see
/// <see cref="SubscriberInfoContentBuilder"/> and <c>docs/server/status-protocol-orders.md</c>.
/// </remarks>
public sealed class SubscriberInfoDownloadProcessor : IDownloadOrderProcessor
{
    private readonly IMasterDataManager _masterData;
    private readonly IEventLog _eventLog;

    /// <summary>Initializes the processor.</summary>
    /// <param name="masterData">The master-data manager the customer/subscriber data is read from.</param>
    /// <param name="eventLog">The append-only event log the generation outcome is written to.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SubscriberInfoDownloadProcessor(IMasterDataManager masterData, IEventLog eventLog)
    {
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(eventLog);

        _masterData = masterData;
        _eventLog = eventLog;
    }

    /// <inheritdoc />
    public bool CanProcess(string? effectiveOrderType) => StatusProtocolOrderTypes.IsSubscriberInfoOrderType(effectiveOrderType);

    /// <inheritdoc />
    public async Task<byte[]?> GenerateAsync(DownloadOrderRequest request, CancellationToken ct = default)
    {
        var subscriber = request.Subscriber;

        var bank = await _masterData.GetBankAsync(subscriber.HostId, ct).ConfigureAwait(false);
        var partner = await _masterData.GetPartnerAsync(subscriber.HostId, subscriber.PartnerId, ct).ConfigureAwait(false);
        if (bank is null || partner is null)
        {
            return null;
        }

        byte[]? content = request.EffectiveOrderType switch
        {
            StatusProtocolOrderTypes.SubscriberData => await BuildHtdAsync(request, bank, partner, ct).ConfigureAwait(false),
            StatusProtocolOrderTypes.CustomerData => await BuildHkdAsync(request, bank, partner, ct).ConfigureAwait(false),
            StatusProtocolOrderTypes.AvailableOrderTypes => await BuildHaaAsync(request, ct).ConfigureAwait(false),
            StatusProtocolOrderTypes.BankParameters => SubscriberInfoContentBuilder.BuildHpd(request.Version, bank),
            _ => null,
        };

        if (content is null)
        {
            return null;
        }

        await _eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = EbicsEventType.OrderAccepted,
                Severity = EbicsEventSeverity.Info,
                Visibility = EbicsEventVisibility.CustomerVisible,
                HostId = subscriber.HostId,
                PartnerId = subscriber.PartnerId,
                UserId = subscriber.UserId,
                OrderType = request.EffectiveOrderType,
                ReturnCode = EbicsReturnCode.Ok,
                Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} response generated ({1} bytes).",
                    request.EffectiveOrderType,
                    content.Length),
            },
            ct).ConfigureAwait(false);

        return content;
    }

    private async Task<byte[]?> BuildHtdAsync(DownloadOrderRequest request, EBICO.Core.Domain.Bank bank, EBICO.Core.Domain.Partner partner, CancellationToken ct)
    {
        var subscriber = request.Subscriber;
        var user = await _masterData.GetSubscriberAsync(subscriber.HostId, subscriber.PartnerId, subscriber.UserId, ct).ConfigureAwait(false);
        return user is null ? null : SubscriberInfoContentBuilder.BuildHtd(request.Version, bank, partner, user);
    }

    private async Task<byte[]> BuildHkdAsync(DownloadOrderRequest request, EBICO.Core.Domain.Bank bank, EBICO.Core.Domain.Partner partner, CancellationToken ct)
    {
        var subscriber = request.Subscriber;
        var users = await _masterData.GetSubscribersAsync(subscriber.HostId, subscriber.PartnerId, ct).ConfigureAwait(false);
        return SubscriberInfoContentBuilder.BuildHkd(request.Version, bank, partner, users);
    }

    private async Task<byte[]?> BuildHaaAsync(DownloadOrderRequest request, CancellationToken ct)
    {
        var subscriber = request.Subscriber;
        var user = await _masterData.GetSubscriberAsync(subscriber.HostId, subscriber.PartnerId, subscriber.UserId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        // HAA lists the order types the subscriber may download whose data is generated on demand — the
        // statement/report (BTF download) permissions the subscriber holds.
        var downloadOrderTypes = user.Permissions
            .Select(p => p.OrderType)
            .Distinct(StringComparer.Ordinal)
            .Where(BtfOrderTypeCatalog.IsDownloadOrderType)
            .OrderBy(o => o, StringComparer.Ordinal)
            .ToArray();

        return SubscriberInfoContentBuilder.BuildHaa(request.Version, downloadOrderTypes);
    }
}
