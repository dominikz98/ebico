using System.Collections.Concurrent;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// A thread-safe in-memory <see cref="IServerBankKeyStore"/>. This is the default registration and
/// the natural choice for the emulator and tests: a bank key pair is generated on first access per
/// <see cref="HostId"/> and cached for the process lifetime (so HPB is stable across calls); nothing
/// is persisted across restarts.
/// </summary>
public sealed class InMemoryServerBankKeyStore : IServerBankKeyStore
{
    private readonly ConcurrentDictionary<HostId, BankKeyPair> _keys = new();

    /// <inheritdoc />
    public Task<BankKeyPair> GetOrCreateAsync(HostId host, CancellationToken ct = default)
        => Task.FromResult(_keys.GetOrAdd(host, static _ => Generate()));

    /// <inheritdoc />
    public Task SetAsync(HostId host, BankKeyPair keys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys[host] = keys;
        return Task.CompletedTask;
    }

    // Generates a fresh bank key pair with the current default versions (X002 / E002, permitted for
    // every supported protocol version). The RSA key material is the same across versions; only the
    // key-version code differs, and X002/E002 are the version-agnostic defaults.
    private static BankKeyPair Generate() => new(
        RsaKeyMaterial.Generate(),
        KeyVersions.Default(KeyPurpose.Authentication, EbicsVersion.H005).Version,
        RsaKeyMaterial.Generate(),
        KeyVersions.Default(KeyPurpose.Encryption, EbicsVersion.H005).Version);
}
