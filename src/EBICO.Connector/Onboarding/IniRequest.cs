using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding;

/// <summary>
/// Sends the subscriber's public <b>signature</b> key (bank-technical, version <c>A00x</c>) to the
/// bank — the EBICS <c>INI</c> order. The private/public key pair must already exist in the
/// <see cref="EBICO.Connector.Keys.IKeyStore"/> (generate it with <c>ISubscriberKeyGenerator</c> first).
/// </summary>
/// <remarks>
/// INI is an <em>unsecured</em> request: the order data is compressed and base64-encoded but neither
/// encrypted nor authentication-signed. Trust is established out of band via the INI letter
/// (<see cref="IniResult.Letter"/>), whose key fingerprint the bank compares against what it receives.
/// </remarks>
public sealed class IniRequest : IEbicsRequest<IniResult>
{
    /// <summary>
    /// The signature key version to declare. When <see langword="null"/> the connector uses the
    /// default for the connection's EBICS version (<c>A005</c>; <c>A006</c> is opt-in).
    /// </summary>
    public KeyVersion? SignatureVersion { get; init; }

    /// <summary>Whether to render the INI initialization letter into <see cref="IniResult.Letter"/>.</summary>
    public bool IncludeLetter { get; init; } = true;
}
