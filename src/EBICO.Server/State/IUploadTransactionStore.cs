namespace EBICO.Server.State;

/// <summary>
/// Holds the in-flight (and, until evicted, completed) server-side upload transactions of the
/// transaction engine (issue #32), keyed by the upper-case hex form of the 16-byte transaction id
/// (<see cref="UploadTransaction.TransactionIdHex"/>). The initialisation phase creates an entry; the
/// transfer phase looks it up by transaction id and buffers segments into it.
/// </summary>
/// <remarks>
/// This is the transaction-scoped counterpart to the master-data <see cref="IEbicsStateStore"/> (which
/// holds banks/partners/subscribers) and the key stores. The default registration is the thread-safe
/// in-memory <see cref="InMemoryUploadTransactionStore"/>; a persistent or evicting implementation can
/// be substituted via <c>TryAddSingleton</c> before <c>AddEbicoServer</c>. Eviction/TTL of orphaned or
/// completed transactions is a recovery concern (issue #35).
/// </remarks>
public interface IUploadTransactionStore
{
    /// <summary>Adds a freshly initialised <paramref name="transaction"/> to the store.</summary>
    /// <param name="transaction">The transaction to store, keyed by its <see cref="UploadTransaction.TransactionIdHex"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A transaction with the same id already exists (an id collision).</exception>
    void Create(UploadTransaction transaction);

    /// <summary>Looks up the transaction with the given hex transaction id.</summary>
    /// <param name="transactionIdHex">The upper-case hex form of the transaction id.</param>
    /// <param name="transaction">The matching transaction when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a transaction was found; otherwise <see langword="false"/>.</returns>
    bool TryGet(string transactionIdHex, out UploadTransaction? transaction);

    /// <summary>Removes the transaction with the given hex transaction id, if present.</summary>
    /// <param name="transactionIdHex">The upper-case hex form of the transaction id.</param>
    /// <returns><see langword="true"/> when a transaction was removed; otherwise <see langword="false"/>.</returns>
    bool Remove(string transactionIdHex);

    /// <summary>The number of transactions currently held.</summary>
    int Count { get; }
}
