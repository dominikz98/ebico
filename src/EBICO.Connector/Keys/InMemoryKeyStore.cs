using System.Collections.Concurrent;
using EBICO.Core.Crypto;

namespace EBICO.Connector.Keys;

/// <summary>
/// A thread-safe in-memory <see cref="IKeyStore"/>. This is the default registration and the
/// natural choice for tests and short-lived processes; nothing is persisted across restarts.
/// </summary>
public sealed class InMemoryKeyStore : IKeyStore
{
    private readonly ConcurrentDictionary<(KeyOwner Owner, KeyPurpose Purpose), RsaKeyMaterial> _keys = new();

    /// <inheritdoc />
    public Task<RsaKeyMaterial?> GetAsync(KeyOwner owner, KeyPurpose purpose, CancellationToken ct = default)
    {
        _keys.TryGetValue((owner, purpose), out var material);
        return Task.FromResult(material);
    }

    /// <inheritdoc />
    public Task StoreAsync(KeyOwner owner, KeyPurpose purpose, RsaKeyMaterial material, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(material);
        _keys[(owner, purpose)] = material;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ContainsAsync(KeyOwner owner, KeyPurpose purpose, CancellationToken ct = default)
        => Task.FromResult(_keys.ContainsKey((owner, purpose)));
}
