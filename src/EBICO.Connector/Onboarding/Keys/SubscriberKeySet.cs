namespace EBICO.Connector.Onboarding.Keys;

/// <summary>
/// The result of generating the subscriber's onboarding key pairs: the key versions and SHA-256
/// fingerprints of the signature (<c>A00x</c>), authentication (<c>X00x</c>) and encryption
/// (<c>E00x</c>) keys. The private keys themselves live in the <see cref="EBICO.Connector.Keys.IKeyStore"/>.
/// </summary>
public sealed class SubscriberKeySet
{
    /// <summary>The signature key version (e.g. <c>"A005"</c>).</summary>
    public required string SignatureKeyVersion { get; init; }

    /// <summary>The authentication key version (e.g. <c>"X002"</c>).</summary>
    public required string AuthenticationKeyVersion { get; init; }

    /// <summary>The encryption key version (e.g. <c>"E002"</c>).</summary>
    public required string EncryptionKeyVersion { get; init; }

    /// <summary>The SHA-256 fingerprint of the signature public key (wire form).</summary>
    public required byte[] SignatureKeyDigest { get; init; }

    /// <summary>The SHA-256 fingerprint of the authentication public key (wire form).</summary>
    public required byte[] AuthenticationKeyDigest { get; init; }

    /// <summary>The SHA-256 fingerprint of the encryption public key (wire form).</summary>
    public required byte[] EncryptionKeyDigest { get; init; }
}
