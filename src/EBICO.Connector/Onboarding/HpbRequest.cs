using System.Security.Cryptography.X509Certificates;

namespace EBICO.Connector.Onboarding;

/// <summary>
/// Downloads the bank's public <b>authentication</b> (<c>X00x</c>) and <b>encryption</b>
/// (<c>E00x</c>) keys — the EBICS <c>HPB</c> order — verifies them against the bank letter and stores
/// them in the <see cref="EBICO.Connector.Keys.IKeyStore"/> (owner <c>Bank</c>).
/// </summary>
/// <remarks>
/// HPB is the only onboarding flow that is authentication-signed (X002) and whose response is
/// encrypted (E002); the subscriber's X002 and E002 key pairs must therefore already be present.
/// Supplying the expected fingerprints from the bank letter makes the connector verify them
/// <em>in the flow</em> (a mismatch is a security failure, thrown as an exception).
/// </remarks>
public sealed class HpbRequest : IEbicsRequest<HpbResult>
{
    /// <summary>
    /// The expected SHA-256 fingerprint of the bank's authentication key (from the bank letter). When
    /// set, a mismatch aborts the flow with an exception; when <see langword="null"/> the fingerprint
    /// is returned unverified in <see cref="HpbResult"/> for the caller to compare.
    /// </summary>
    public ReadOnlyMemory<byte>? ExpectedAuthenticationKeyDigest { get; init; }

    /// <summary>The expected SHA-256 fingerprint of the bank's encryption key (from the bank letter).</summary>
    public ReadOnlyMemory<byte>? ExpectedEncryptionKeyDigest { get; init; }

    /// <summary>
    /// Trust anchors for X.509 verification of the bank certificates (H005 only). When
    /// <see langword="null"/> no chain verification is performed and trust rests on the fingerprint
    /// comparison alone. Ignored for H003/H004 (pure-key procedures).
    /// </summary>
    public X509Certificate2Collection? TrustAnchors { get; init; }

    /// <summary>Whether to store the received bank keys in the key store. Defaults to <see langword="true"/>.</summary>
    public bool StoreBankKeys { get; init; } = true;
}
