namespace EBICO.Connector.Onboarding;

/// <summary>The outcome of an <see cref="HpbRequest"/>: the bank's public keys and their fingerprints.</summary>
public sealed class HpbResult
{
    /// <summary>The bank's public authentication and encryption keys.</summary>
    public required BankKeys BankKeys { get; init; }

    /// <summary>The SHA-256 fingerprint of the bank's authentication key, in wire form (raw bytes).</summary>
    public required byte[] AuthenticationKeyDigest { get; init; }

    /// <summary>The SHA-256 fingerprint of the bank's encryption key, in wire form (raw bytes).</summary>
    public required byte[] EncryptionKeyDigest { get; init; }

    /// <summary>The bank authentication key fingerprint rendered (uppercase hex, grouped) for comparison against the bank letter.</summary>
    public required string AuthenticationKeyDigestText { get; init; }

    /// <summary>The bank encryption key fingerprint rendered (uppercase hex, grouped) for comparison against the bank letter.</summary>
    public required string EncryptionKeyDigestText { get; init; }

    /// <summary>
    /// Whether the received bank fingerprints were verified against the expected values supplied on
    /// the request. <see langword="false"/> when no expected fingerprints were provided.
    /// </summary>
    public required bool FingerprintsVerified { get; init; }
}
