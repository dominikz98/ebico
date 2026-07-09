using System.Security.Cryptography.X509Certificates;
using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding;

/// <summary>
/// The bank's public keys as obtained from an <c>HPB</c> response: the authentication (<c>X00x</c>)
/// and encryption (<c>E00x</c>) keys, optionally with the X.509 certificates they were carried in
/// (H005). Used as the value of an <see cref="HpbResult"/> and stored under
/// <see cref="EBICO.Connector.Keys.KeyOwner.Bank"/> in the key store.
/// </summary>
public sealed class BankKeys
{
    /// <summary>The bank's public authentication key (<c>X00x</c>).</summary>
    public required RsaKeyMaterial Authentication { get; init; }

    /// <summary>The bank's public encryption key (<c>E00x</c>).</summary>
    public required RsaKeyMaterial Encryption { get; init; }

    /// <summary>The X.509 certificate carrying the authentication key (H005), or <see langword="null"/> for pure-key procedures.</summary>
    public X509Certificate2? AuthenticationCertificate { get; init; }

    /// <summary>The X.509 certificate carrying the encryption key (H005), or <see langword="null"/> for pure-key procedures.</summary>
    public X509Certificate2? EncryptionCertificate { get; init; }

    /// <summary>The bank host identifier (<c>HostID</c>) reported in the HPB response.</summary>
    public required string HostId { get; init; }
}
