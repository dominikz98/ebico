using EBICO.Core.Crypto;

namespace EBICO.Suite.Services;

/// <summary>
/// Builds <see cref="KeyView"/> read-model entries from crypto primitives, computing the SHA-256
/// public-key fingerprint in the INI-letter rendering. Shared by the live
/// <see cref="EmulatorStateProvider"/> (store-backed keys) and the sample
/// <see cref="SampleEmulatorStateProvider"/> so both produce identical views for the same key.
/// </summary>
public static class KeyViewFactory
{
    /// <summary>
    /// Creates a <see cref="KeyView"/> for <paramref name="ownerLabel"/> from the given key version and
    /// public key. Any private components are dropped (<see cref="RsaKeyMaterial.ToPublicOnly"/>), since
    /// the view — and the DTO contract — only ever expose public material.
    /// </summary>
    /// <param name="ownerLabel">Human-readable owner label, e.g. <c>"Teilnehmer PARTNER01 / USER0001"</c>.</param>
    /// <param name="version">The EBICS key version (its <see cref="KeyVersion.Purpose"/> and code drive the view).</param>
    /// <param name="publicKey">The RSA key material; reduced to its public part.</param>
    /// <returns>The populated <see cref="KeyView"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="publicKey"/> is <see langword="null"/>.</exception>
    public static KeyView Create(string ownerLabel, KeyVersion version, RsaKeyMaterial publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        var material = publicKey.ToPublicOnly();
        var digest = PublicKeyFingerprint.Compute(material);
        return new KeyView
        {
            OwnerLabel = ownerLabel,
            Purpose = version.Purpose,
            KeyVersion = version.Value,
            PublicKey = material,
            FingerprintText = PublicKeyFingerprint.ToLetterFormat(digest),
        };
    }
}
