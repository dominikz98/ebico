namespace EBICO.Connector.Onboarding;

/// <summary>The outcome of an <see cref="IniRequest"/>: the submitted signature key's version and fingerprint.</summary>
public sealed class IniResult
{
    /// <summary>The signature key version that was sent (e.g. <c>"A005"</c>).</summary>
    public required string SignatureKeyVersion { get; init; }

    /// <summary>The SHA-256 fingerprint of the signature public key, in wire form (raw bytes).</summary>
    public required byte[] SignatureKeyDigest { get; init; }

    /// <summary>
    /// The signature key fingerprint rendered for the INI letter (uppercase hex, grouped) for
    /// human comparison at the bank.
    /// </summary>
    public required string SignatureKeyDigestText { get; init; }

    /// <summary>The rendered initialization letter, or <see langword="null"/> when not requested.</summary>
    public InitializationLetter? Letter { get; init; }
}
