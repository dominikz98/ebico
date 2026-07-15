using System.Globalization;
using EBICO.Core.Administrative;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;

namespace EBICO.Server.Orders;

/// <summary>
/// The <see cref="IDownloadOrderProcessor"/> for the customer-protocol download orders HAC and PTK
/// (issue #41). Both are pure projections over the append-only event log (issue #69): the processor reads
/// the requesting customer's <see cref="EbicsEventVisibility.CustomerVisible"/> events (optionally bounded by
/// the requested reporting period), maps them to <see cref="CustomerProtocolEntry"/> and renders HAC as XML
/// (<see cref="HacProtocolBuilder"/>) or PTK as text (<see cref="PtkProtocolBuilder"/>).
/// </summary>
/// <remarks>
/// The generation itself is logged only as an <see cref="EbicsEventVisibility.Internal"/> event so it is not
/// double-counted as a customer-visible order acceptance. (The download transaction's own
/// <c>DownloadStarted</c>/<c>DownloadCompleted</c> lifecycle events stay customer-visible, like any download,
/// so a protocol fetch is itself visible in later protocols.) See the Spec-Vorbehalte on the two builders.
/// </remarks>
public sealed class CustomerProtocolDownloadProcessor : IDownloadOrderProcessor
{
    private readonly IEventLog _eventLog;

    /// <summary>Initializes the processor.</summary>
    /// <param name="eventLog">The append-only event log the customer protocol is projected from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventLog"/> is <see langword="null"/>.</exception>
    public CustomerProtocolDownloadProcessor(IEventLog eventLog)
    {
        ArgumentNullException.ThrowIfNull(eventLog);
        _eventLog = eventLog;
    }

    /// <inheritdoc />
    public bool CanProcess(string? effectiveOrderType) => StatusProtocolOrderTypes.IsCustomerProtocolOrderType(effectiveOrderType);

    /// <inheritdoc />
    public async Task<byte[]?> GenerateAsync(DownloadOrderRequest request, CancellationToken ct = default)
    {
        var subscriber = request.Subscriber;
        var (from, to) = ResolveWindow(request.DateRange);

        var events = await _eventLog
            .QueryAsync(
                new EbicsEventQuery
                {
                    HostId = subscriber.HostId,
                    PartnerId = subscriber.PartnerId,
                    Visibility = EbicsEventVisibility.CustomerVisible,
                    From = from,
                    To = to,
                },
                ct)
            .ConfigureAwait(false);

        var entries = events.Select(ToEntry).ToArray();

        var content = request.EffectiveOrderType == StatusProtocolOrderTypes.CustomerProtocolText
            ? PtkProtocolBuilder.Build(entries)
            : HacProtocolBuilder.Build(request.Version, entries);

        await _eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = EbicsEventType.OrderAccepted,
                Severity = EbicsEventSeverity.Info,
                Visibility = EbicsEventVisibility.Internal,
                HostId = subscriber.HostId,
                PartnerId = subscriber.PartnerId,
                UserId = subscriber.UserId,
                OrderType = request.EffectiveOrderType,
                ReturnCode = EbicsReturnCode.Ok,
                Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} customer protocol generated ({1} entries, {2} bytes).",
                    request.EffectiveOrderType,
                    entries.Length,
                    content.Length),
            },
            ct).ConfigureAwait(false);

        return content;
    }

    private static CustomerProtocolEntry ToEntry(EbicsEvent evt)
        => new(
            evt.Sequence,
            evt.Timestamp,
            evt.OrderType,
            evt.ReturnCode?.Code,
            evt.ReturnCode?.SymbolicName,
            evt.Severity.ToString(),
            evt.Message);

    // The event-log query window: the request's reporting period, if any, mapped to an inclusive lower and
    // an exclusive upper bound (EbicsEventQuery.To is exclusive), both at UTC midnight.
    private static (DateTimeOffset? From, DateTimeOffset? To) ResolveWindow(EBICO.Core.DateRange? range)
    {
        if (range is not { } value)
        {
            return (null, null);
        }

        DateTimeOffset? from = value.Start is { } start
            ? new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        DateTimeOffset? to = value.End is { } end
            ? new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

        return (from, to);
    }
}
