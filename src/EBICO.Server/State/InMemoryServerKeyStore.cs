using System.Collections.Concurrent;
using EBICO.Core.Crypto;

namespace EBICO.Server.State;

/// <summary>
/// A thread-safe in-memory <see cref="IServerKeyStore"/>. This is the default registration and the
/// natural choice for the emulator and tests; nothing is persisted across restarts.
/// </summary>
public sealed class InMemoryServerKeyStore : IServerKeyStore
{
    private readonly ConcurrentDictionary<(SubscriberKeyRef Subscriber, KeyPurpose Purpose), StoredPublicKey> _keys = new();

    /// <inheritdoc />
    public Task StoreAsync(SubscriberKeyRef subscriber, StoredPublicKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        _keys[(subscriber, key.Purpose)] = key;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<StoredPublicKey?> GetAsync(SubscriberKeyRef subscriber, KeyPurpose purpose, CancellationToken ct = default)
    {
        _keys.TryGetValue((subscriber, purpose), out var key);
        return Task.FromResult(key);
    }

    /// <inheritdoc />
    public Task<bool> ContainsAsync(SubscriberKeyRef subscriber, KeyPurpose purpose, CancellationToken ct = default)
        => Task.FromResult(_keys.ContainsKey((subscriber, purpose)));
}
