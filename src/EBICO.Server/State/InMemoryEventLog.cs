using Microsoft.Extensions.Options;

namespace EBICO.Server.State;

/// <summary>
/// Default in-memory <see cref="IEventLog"/>. Thread-safe (a single lock guards the append log and the
/// monotonic sequence counter), backed by a FIFO queue whose enumeration order equals the append/sequence
/// order. Nothing is persisted across process restarts.
/// </summary>
/// <remarks>
/// Memory is bounded by <see cref="EbicoServerOptions.MaxEventLogEntries"/>: once the cap is reached, the
/// oldest event is dropped as a new one is appended (a ring buffer). A cap of <c>0</c> disables the bound
/// (the log grows until the process restarts). Sequence numbers keep increasing regardless of eviction, so
/// they stay a stable, gap-tolerant total order. A persistent store can replace this via
/// <c>TryAddSingleton&lt;IEventLog, …&gt;</c> before <c>AddEbicoServer</c> (ADR-0011/ADR-0015).
/// </remarks>
public sealed class InMemoryEventLog : IEventLog
{
    private readonly object _gate = new();
    private readonly Queue<EbicsEvent> _events = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _maxEntries;
    private long _sequence;

    /// <summary>Initializes the in-memory event log.</summary>
    /// <param name="timeProvider">The clock used to stamp <see cref="EbicsEvent.Timestamp"/> on append.</param>
    /// <param name="options">The server options; <see cref="EbicoServerOptions.MaxEventLogEntries"/> bounds the log size.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public InMemoryEventLog(TimeProvider timeProvider, IOptions<EbicoServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _timeProvider = timeProvider;
        _maxEntries = options.Value.MaxEventLogEntries;
    }

    /// <inheritdoc />
    public Task AppendAsync(EbicsEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        lock (_gate)
        {
            var stamped = evt with
            {
                Sequence = ++_sequence,
                Timestamp = _timeProvider.GetUtcNow(),
            };

            _events.Enqueue(stamped);

            // Ring-buffer eviction: keep at most _maxEntries events (0 = unbounded).
            while (_maxEntries > 0 && _events.Count > _maxEntries)
            {
                _events.Dequeue();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EbicsEvent>> QueryAsync(EbicsEventQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = new List<EbicsEvent>();
        lock (_gate)
        {
            // The queue enumerates FIFO, i.e. in ascending Sequence order; no explicit sort needed.
            foreach (var evt in _events)
            {
                if (!Matches(evt, query))
                {
                    continue;
                }

                results.Add(evt);
                if (query.Limit is { } limit && limit > 0 && results.Count >= limit)
                {
                    break;
                }
            }
        }

        return Task.FromResult<IReadOnlyList<EbicsEvent>>(results);
    }

    private static bool Matches(EbicsEvent evt, EbicsEventQuery query)
    {
        if (query.HostId is { } hostId && evt.HostId != hostId)
        {
            return false;
        }

        if (query.PartnerId is { } partnerId && evt.PartnerId != partnerId)
        {
            return false;
        }

        if (query.UserId is { } userId && evt.UserId != userId)
        {
            return false;
        }

        if (query.Type is { } type && evt.Type != type)
        {
            return false;
        }

        if (query.Visibility is { } visibility && evt.Visibility != visibility)
        {
            return false;
        }

        // From is inclusive, To is exclusive.
        if (query.From is { } from && evt.Timestamp < from)
        {
            return false;
        }

        if (query.To is { } to && evt.Timestamp >= to)
        {
            return false;
        }

        return true;
    }
}
