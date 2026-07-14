using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EBICO.Server.Transactions;

/// <summary>
/// Background service that periodically sweeps the transaction stores for expired (idle-timed-out)
/// transactions (issue #35). It calls <see cref="ITransactionEvictor.EvictExpiredAsync"/> on every
/// registered evictor (the upload and download engines) every
/// <see cref="EbicoServerOptions.TransactionCleanupInterval"/>. This bounds memory even for orphaned
/// transactions the client never touches again; the same eviction also happens lazily on the request
/// path when an expired transaction is accessed.
/// </summary>
/// <remarks>
/// A non-positive interval disables the sweeper (lazy expiry still applies). Each evictor is swept inside
/// its own try/catch so a single failure neither aborts the sweep nor tears down the host (an unhandled
/// <see cref="BackgroundService"/> exception would stop the host by default).
/// </remarks>
public sealed class TransactionCleanupService : BackgroundService
{
    private readonly IReadOnlyList<ITransactionEvictor> _evictors;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TransactionCleanupService> _logger;
    private readonly TimeSpan _interval;

    /// <summary>Initializes the cleanup service.</summary>
    /// <param name="evictors">The transaction evictors to sweep (the upload and download engines).</param>
    /// <param name="timeProvider">The clock driving the sweep timer (fakeable in tests).</param>
    /// <param name="options">The server options supplying the cleanup interval.</param>
    /// <param name="logger">The logger used to report a failed sweep.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public TransactionCleanupService(
        IEnumerable<ITransactionEvictor> evictors,
        TimeProvider timeProvider,
        IOptions<EbicoServerOptions> options,
        ILogger<TransactionCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(evictors);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _evictors = evictors.ToArray();
        _timeProvider = timeProvider;
        _logger = logger;
        _interval = options.Value.TransactionCleanupInterval;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A non-positive interval disables the background sweeper; lazy expiry on access still applies.
        if (_interval <= TimeSpan.Zero)
        {
            return;
        }

        using var timer = new PeriodicTimer(_interval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                foreach (var evictor in _evictors)
                {
                    try
                    {
                        await evictor.EvictExpiredAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A sweep failure must not abort the loop or stop the host.
                        _logger.LogError(ex, "Transaction cleanup sweep failed for {Evictor}.", evictor.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown: the host cancelled the stopping token.
        }
    }
}
