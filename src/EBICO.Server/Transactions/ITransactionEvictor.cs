namespace EBICO.Server.Transactions;

/// <summary>
/// A transaction engine that can evict its expired (idle-timed-out) transactions on demand (issue #35).
/// Implemented by both the upload and download engines and consumed by the background
/// <c>TransactionCleanupService</c>, which sweeps every registered evictor periodically. Keeping this a
/// separate, narrow abstraction leaves the request-carrying engine interfaces
/// (<see cref="IUploadTransactionEngine"/>/<see cref="IDownloadTransactionEngine"/>) untouched.
/// </summary>
/// <remarks>
/// The eviction policy (the idle timeout) lives in the engine, not the store; evicting a download
/// re-enqueues its (already dequeued) order data so it is not lost. The same logic runs lazily on the
/// request path when a transaction is found to have expired.
/// </remarks>
public interface ITransactionEvictor
{
    /// <summary>Removes every transaction that has idled past the configured timeout.</summary>
    /// <param name="ct">A token to cancel the sweep.</param>
    /// <returns>The number of transactions evicted.</returns>
    Task<int> EvictExpiredAsync(CancellationToken ct = default);
}
