using System.Globalization;
using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Statements;
using EBICO.Server.State;

namespace EBICO.Server.Orders;

/// <summary>
/// The default <see cref="IDownloadOrderProcessor"/>: generates deterministic synthetic account statements
/// and reports (STA/VMK/C53/C52/C54, issue #40) via <see cref="StatementContentFactory"/>. The requested
/// reporting period is honoured when present; otherwise a default trailing window ending at the current time
/// is applied. The generation outcome is recorded on the event log.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the content is synthetic test data (the "generierbare Testdaten serverseitig"),
/// not drawn from a real account/booking store, and the produced MT/camt formats are minimal, structurally
/// plausible renderings (see the individual builders). See <c>docs/server/statement-orders.md</c> and ADR-0018.
/// </remarks>
public sealed class StatementDownloadProcessor : IDownloadOrderProcessor
{
    /// <summary>The number of days in the default reporting window applied when the request omits the range start.</summary>
    public const int DefaultWindowDays = 30;

    private readonly IEventLog _eventLog;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the processor.</summary>
    /// <param name="eventLog">The append-only event log the generation outcome is written to.</param>
    /// <param name="timeProvider">The clock used to stamp the statement and to resolve an open reporting window.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public StatementDownloadProcessor(IEventLog eventLog, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(eventLog);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _eventLog = eventLog;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public bool CanProcess(string? effectiveOrderType) => StatementOrderTypes.IsStatementOrderType(effectiveOrderType);

    /// <inheritdoc />
    public async Task<byte[]?> GenerateAsync(DownloadOrderRequest request, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var (start, end) = ResolveWindow(request.DateRange, now);

        var content = StatementContentFactory.Create(
            request.EffectiveOrderType,
            request.Subscriber.HostId.Value,
            request.Subscriber.PartnerId.Value,
            request.Subscriber.UserId.Value,
            start,
            end,
            now);

        await _eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = EbicsEventType.OrderAccepted,
                Severity = EbicsEventSeverity.Info,
                Visibility = EbicsEventVisibility.CustomerVisible,
                HostId = request.Subscriber.HostId,
                PartnerId = request.Subscriber.PartnerId,
                UserId = request.Subscriber.UserId,
                OrderType = request.EffectiveOrderType,
                ReturnCode = EbicsReturnCode.Ok,
                Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} statement generated for [{1:yyyy-MM-dd}, {2:yyyy-MM-dd}] ({3} bytes, ZIP container).",
                    request.EffectiveOrderType,
                    start,
                    end,
                    content.Length),
            },
            ct).ConfigureAwait(false);

        return content;
    }

    // Resolves the reporting window: both bounds honoured when present; a missing start defaults to
    // DefaultWindowDays before the end; a missing end defaults to today. Guards against an inverted range.
    private static (DateOnly Start, DateOnly End) ResolveWindow(DateRange? range, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var end = range?.End ?? today;
        var start = range?.Start ?? end.AddDays(-DefaultWindowDays);
        return start > end ? (end, end) : (start, end);
    }
}
