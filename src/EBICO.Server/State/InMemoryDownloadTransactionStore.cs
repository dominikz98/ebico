using System.Collections.Concurrent;

namespace EBICO.Server.State;

/// <summary>
/// A thread-safe in-memory <see cref="IDownloadTransactionStore"/>. The default registration and the
/// natural choice for the emulator and tests; nothing is persisted across restarts. Idle expiry
/// (issue #35) is driven by the engine/cleanup service via <see cref="GetAll"/> and
/// <see cref="Remove"/>; this store itself holds no eviction policy.
/// </summary>
public sealed class InMemoryDownloadTransactionStore : IDownloadTransactionStore
{
    private readonly ConcurrentDictionary<string, DownloadTransaction> _transactions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Create(DownloadTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!_transactions.TryAdd(transaction.TransactionIdHex, transaction))
        {
            throw new InvalidOperationException(
                $"A download transaction with id '{transaction.TransactionIdHex}' already exists.");
        }
    }

    /// <inheritdoc />
    public bool TryGet(string transactionIdHex, out DownloadTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(transactionIdHex);

        var found = _transactions.TryGetValue(transactionIdHex, out var value);
        transaction = value;
        return found;
    }

    /// <inheritdoc />
    public bool Remove(string transactionIdHex)
    {
        ArgumentNullException.ThrowIfNull(transactionIdHex);
        return _transactions.TryRemove(transactionIdHex, out _);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<DownloadTransaction> GetAll() => _transactions.Values.ToArray();

    /// <inheritdoc />
    public int Count => _transactions.Count;
}
