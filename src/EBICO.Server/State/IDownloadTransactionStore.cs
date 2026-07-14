namespace EBICO.Server.State;

/// <summary>
/// Holds the in-flight (and, until acknowledged, completed) server-side download transactions of the
/// download transaction engine (issue #33), keyed by the upper-case hex form of the 16-byte transaction
/// id (<see cref="DownloadTransaction.TransactionIdHex"/>). The initialisation phase creates an entry
/// (with the whole segmented, encrypted payload); the transfer phase looks it up to serve segments; the
/// receipt phase removes it.
/// </summary>
/// <remarks>
/// This is the download-side counterpart to <see cref="IUploadTransactionStore"/>. The default
/// registration is the thread-safe in-memory <see cref="InMemoryDownloadTransactionStore"/>; a
/// persistent or evicting implementation can be substituted via <c>TryAddSingleton</c> before
/// <c>AddEbicoServer</c>. Eviction/TTL of orphaned or completed transactions is a recovery concern
/// (issue #35).
/// </remarks>
public interface IDownloadTransactionStore
{
    /// <summary>Adds a freshly initialised <paramref name="transaction"/> to the store.</summary>
    /// <param name="transaction">The transaction to store, keyed by its <see cref="DownloadTransaction.TransactionIdHex"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A transaction with the same id already exists (an id collision).</exception>
    void Create(DownloadTransaction transaction);

    /// <summary>Looks up the transaction with the given hex transaction id.</summary>
    /// <param name="transactionIdHex">The upper-case hex form of the transaction id.</param>
    /// <param name="transaction">The matching transaction when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a transaction was found; otherwise <see langword="false"/>.</returns>
    bool TryGet(string transactionIdHex, out DownloadTransaction? transaction);

    /// <summary>Removes the transaction with the given hex transaction id, if present.</summary>
    /// <param name="transactionIdHex">The upper-case hex form of the transaction id.</param>
    /// <returns><see langword="true"/> when a transaction was removed; otherwise <see langword="false"/>.</returns>
    bool Remove(string transactionIdHex);

    /// <summary>
    /// Returns a point-in-time snapshot of all held transactions. Used by the background cleanup service
    /// to find expired transactions (the eviction policy lives in the engine, not the store); the
    /// snapshot is decoupled from the store, so iterating it while other threads create/remove is safe.
    /// </summary>
    /// <returns>A snapshot of the currently held transactions (empty when none).</returns>
    IReadOnlyCollection<DownloadTransaction> GetAll();

    /// <summary>The number of transactions currently held.</summary>
    int Count { get; }
}
