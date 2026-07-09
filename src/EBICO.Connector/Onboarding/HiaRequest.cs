using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding;

/// <summary>
/// Sends the subscriber's public <b>authentication</b> (<c>X00x</c>) and <b>encryption</b>
/// (<c>E00x</c>) keys to the bank — the EBICS <c>HIA</c> order. The key pairs must already exist in
/// the <see cref="EBICO.Connector.Keys.IKeyStore"/> (generate them with <c>ISubscriberKeyGenerator</c>).
/// </summary>
/// <remarks>Like INI, HIA is an <em>unsecured</em> request (compressed + base64, not encrypted/signed).</remarks>
public sealed class HiaRequest : IEbicsRequest<HiaResult>
{
    /// <summary>The authentication key version to declare; <see langword="null"/> uses the version default (<c>X002</c>).</summary>
    public KeyVersion? AuthenticationVersion { get; init; }

    /// <summary>The encryption key version to declare; <see langword="null"/> uses the version default (<c>E002</c>).</summary>
    public KeyVersion? EncryptionVersion { get; init; }

    /// <summary>Whether to render the HIA initialization letter into <see cref="HiaResult.Letter"/>.</summary>
    public bool IncludeLetter { get; init; } = true;
}
