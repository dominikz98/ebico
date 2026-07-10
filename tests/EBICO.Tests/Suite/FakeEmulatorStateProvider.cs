using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Suite.Services;

namespace EBICO.Tests.Suite;

/// <summary>
/// Reusable in-memory <see cref="IEmulatorStateProvider"/> fake for the key/certificate view tests
/// (issue #55): banks/partners/subscribers are empty by default; the key list is supplied per test.
/// </summary>
internal sealed class FakeEmulatorStateProvider(IReadOnlyList<KeyView> keys) : IEmulatorStateProvider
{
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Bank>>([]);

    public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Partner>>([]);

    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Subscriber>>([]);

    public Task<IReadOnlyList<KeyView>> GetKeysAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(keys);

    /// <summary>Builds a <see cref="KeyView"/> from a freshly generated RSA key for test input.</summary>
    public static KeyView SampleKey(string ownerLabel, KeyPurpose purpose, string keyVersion)
    {
        var material = RsaKeyMaterial.Generate().ToPublicOnly();
        var digest = PublicKeyFingerprint.Compute(material);
        return new KeyView
        {
            OwnerLabel = ownerLabel,
            Purpose = purpose,
            KeyVersion = keyVersion,
            PublicKey = material,
            FingerprintText = PublicKeyFingerprint.ToLetterFormat(digest),
        };
    }
}
