namespace EBICO.Server.State;

/// <summary>
/// A thread-safe in-memory <see cref="IDownloadDataProvider"/>: a FIFO queue of pending download
/// payloads per (subscriber, order type). The default registration and the natural choice for the
/// emulator and tests; payloads are seeded via the admin API (or directly) and consumed by download
/// initialisations. Nothing is persisted across restarts.
/// </summary>
public sealed class InMemoryDownloadDataProvider : IDownloadDataProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<(SubscriberKeyRef Subscriber, string OrderType), Queue<byte[]>> _queues = [];

    /// <inheritdoc />
    public Task EnqueueAsync(SubscriberKeyRef subscriber, string orderType, byte[] orderData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderType);
        ArgumentNullException.ThrowIfNull(orderData);

        lock (_gate)
        {
            var key = (subscriber, orderType);
            if (!_queues.TryGetValue(key, out var queue))
            {
                queue = new Queue<byte[]>();
                _queues[key] = queue;
            }

            queue.Enqueue(orderData);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]?> TryDequeueAsync(DownloadDataRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.OrderType);

        lock (_gate)
        {
            var key = (request.Subscriber, request.OrderType);
            if (_queues.TryGetValue(key, out var queue) && queue.TryDequeue(out var data))
            {
                return Task.FromResult<byte[]?>(data);
            }
        }

        return Task.FromResult<byte[]?>(null);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(SubscriberKeyRef subscriber, string orderType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderType);

        lock (_gate)
        {
            var count = _queues.TryGetValue((subscriber, orderType), out var queue) ? queue.Count : 0;
            return Task.FromResult(count);
        }
    }
}
