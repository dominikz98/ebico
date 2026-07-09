using EBICO.Core.Crypto;

namespace EBICO.Connector.Keys;

/// <summary>
/// A simple file-backed <see cref="IKeyStore"/>. Each key is one file under a configured
/// directory, named <c>{owner}-{purpose}.key</c>. Subscriber keys are written as unencrypted
/// PKCS#8 (private) and bank keys as SubjectPublicKeyInfo (public), using
/// <see cref="RsaKeyImportExport"/>.
/// </summary>
/// <remarks>
/// <b>Security note:</b> private keys are stored <em>unencrypted</em> on disk. This store is
/// intended for development and simple setups only; production deployments should use an
/// encrypted store or an HSM-backed <see cref="IKeyStore"/> (later issues).
/// </remarks>
public sealed class FileKeyStore : IKeyStore, IDisposable
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a file key store rooted at <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory that holds the key files. Created on first write.</param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is <see langword="null"/> or empty.</exception>
    public FileKeyStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <inheritdoc />
    public async Task<RsaKeyMaterial?> GetAsync(KeyOwner owner, KeyPurpose purpose, CancellationToken ct = default)
    {
        var path = PathFor(owner, purpose);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return Import(owner, bytes);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StoreAsync(KeyOwner owner, KeyPurpose purpose, RsaKeyMaterial material, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        var bytes = Export(owner, material);
        var path = PathFor(owner, purpose);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<bool> ContainsAsync(KeyOwner owner, KeyPurpose purpose, CancellationToken ct = default)
        => Task.FromResult(File.Exists(PathFor(owner, purpose)));

    /// <summary>Releases the internal synchronization primitive.</summary>
    public void Dispose() => _gate.Dispose();

    private string PathFor(KeyOwner owner, KeyPurpose purpose)
        => Path.Combine(_directory, $"{owner}-{purpose}.key".ToLowerInvariant());

    private static byte[] Export(KeyOwner owner, RsaKeyMaterial material)
        => owner == KeyOwner.Subscriber
            ? RsaKeyImportExport.ExportPkcs8(material)
            : RsaKeyImportExport.ExportSubjectPublicKeyInfo(material);

    private static RsaKeyMaterial Import(KeyOwner owner, byte[] bytes)
        => owner == KeyOwner.Subscriber
            ? RsaKeyImportExport.ImportPkcs8(bytes)
            : RsaKeyImportExport.ImportSubjectPublicKeyInfo(bytes);
}
