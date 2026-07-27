namespace EBICO.Suite.Services;

/// <summary>
/// The default <see cref="IMasterDataChangeNotifier"/>: a thread-safe subscriber list broadcasting to
/// every registered handler (issue #126).
/// </summary>
/// <remarks>
/// Registered as a singleton, so subscriptions come from several circuits concurrently and the list is
/// guarded by a lock. <see cref="NotifyChangedAsync"/> broadcasts over a snapshot, so a handler that
/// unsubscribes (or subscribes) during the broadcast cannot disturb the iteration.
/// </remarks>
public sealed class MasterDataChangeNotifier : IMasterDataChangeNotifier
{
    private readonly List<Func<Task>> _handlers = [];
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public IDisposable Subscribe(Func<Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            _handlers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    /// <inheritdoc />
    public async Task NotifyChangedAsync()
    {
        Func<Task>[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _handlers];
        }

        List<Exception>? failures = null;
        foreach (var handler in snapshot)
        {
            try
            {
                await handler().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Keep broadcasting: one stale island must not stop the others from refreshing.
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more master-data change subscribers failed to refresh.", failures);
        }
    }

    private void Unsubscribe(Func<Task> handler)
    {
        lock (_gate)
        {
            _handlers.Remove(handler);
        }
    }

    private sealed class Subscription(MasterDataChangeNotifier owner, Func<Task> handler) : IDisposable
    {
        private MasterDataChangeNotifier? _owner = owner;

        public void Dispose()
        {
            // Idempotent: a component may be disposed more than once.
            Interlocked.Exchange(ref _owner, null)?.Unsubscribe(handler);
        }
    }
}
