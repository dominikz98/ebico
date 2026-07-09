using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding.Keys;

/// <summary>Options for <see cref="ISubscriberKeyGenerator.GenerateAsync"/>.</summary>
public sealed class SubscriberKeyGenerationOptions
{
    /// <summary>The RSA key size in bits for the generated pairs. Defaults to <see cref="RsaKeyMaterial.MinKeySizeBits"/>.</summary>
    public int KeySizeBits { get; init; } = RsaKeyMaterial.MinKeySizeBits;

    /// <summary>
    /// Whether to replace keys already present in the store. When <see langword="false"/> (the
    /// default) generation fails if any subscriber key already exists, so an accidental re-run
    /// cannot silently invalidate an in-progress onboarding.
    /// </summary>
    public bool Overwrite { get; init; }
}
