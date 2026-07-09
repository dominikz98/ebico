namespace EBICO.Connector.Onboarding;

/// <summary>The outcome of a <see cref="HiaRequest"/>: the submitted authentication and encryption key fingerprints.</summary>
public sealed class HiaResult
{
    /// <summary>The SHA-256 fingerprint of the authentication public key, in wire form (raw bytes).</summary>
    public required byte[] AuthenticationKeyDigest { get; init; }

    /// <summary>The SHA-256 fingerprint of the encryption public key, in wire form (raw bytes).</summary>
    public required byte[] EncryptionKeyDigest { get; init; }

    /// <summary>The authentication key fingerprint rendered for the INI letter (uppercase hex, grouped).</summary>
    public required string AuthenticationKeyDigestText { get; init; }

    /// <summary>The encryption key fingerprint rendered for the INI letter (uppercase hex, grouped).</summary>
    public required string EncryptionKeyDigestText { get; init; }

    /// <summary>The rendered initialization letter, or <see langword="null"/> when not requested.</summary>
    public InitializationLetter? Letter { get; init; }
}
