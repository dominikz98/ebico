using System.Collections.Concurrent;

namespace EBICO.Server.State;

/// <summary>
/// A thread-safe in-memory <see cref="IUploadTransactionStore"/>. The default registration and the
/// natural choice for the emulator and tests; nothing is persisted across restarts and nothing is
/// evicted (a TTL/recovery model is issue #35).
/// </summary>
public sealed class InMemoryUploadTransactionStore : IUploadTransactionStore
{
    private readonly ConcurrentDictionary<string, UploadTransaction> _transactions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Create(UploadTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!_transactions.TryAdd(transaction.TransactionIdHex, transaction))
        {
            throw new InvalidOperationException(
                $"An upload transaction with id '{transaction.TransactionIdHex}' already exists.");
        }
    }

    /// <inheritdoc />
    public bool TryGet(string transactionIdHex, out UploadTransaction? transaction)
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
    public int Count => _transactions.Count;
}
