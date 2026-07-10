using EBICO.Core.Crypto;
using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// The bank's own key pair as held by the server: the <b>authentication</b> key (<c>X00x</c>) and the
/// <b>encryption</b> key (<c>E00x</c>) whose public parts are handed to a subscriber via
/// <c>HPB</c> (issue #28). Unlike the subscriber keys in <see cref="IServerKeyStore"/> these are the
/// bank's <em>own</em> keys and may carry the private part (needed for the response authentication
/// signature / upload decryption that arrive with M4).
/// </summary>
/// <param name="Authentication">The bank's authentication key material (public part is enough for HPB).</param>
/// <param name="AuthenticationVersion">The authentication key version (e.g. <c>X002</c>).</param>
/// <param name="Encryption">The bank's encryption key material (public part is enough for HPB).</param>
/// <param name="EncryptionVersion">The encryption key version (e.g. <c>E002</c>).</param>
public sealed record BankKeyPair(
    RsaKeyMaterial Authentication,
    KeyVersion AuthenticationVersion,
    RsaKeyMaterial Encryption,
    KeyVersion EncryptionVersion);

/// <summary>
/// Holds the bank's own authentication (<c>X00x</c>) and encryption (<c>E00x</c>) key pair per
/// <see cref="HostId"/>. HPB (#28) reads it to return the bank's public keys to an onboarded
/// subscriber. This is the counterpart to <see cref="IServerKeyStore"/> (which holds the
/// <em>subscriber</em> public keys received during INI/HIA): a bank operating several host ids keeps
/// a separate key pair per host.
/// </summary>
/// <remarks>
/// The default registration is <see cref="InMemoryServerBankKeyStore"/>, which generates a key pair
/// on first access and caches it for the process lifetime (stable across repeated HPB calls). A
/// persistent or pre-seeded implementation can be substituted via <c>TryAddSingleton</c> before
/// <c>AddEbicoServer</c>; <see cref="SetAsync"/> lets a caller inject a known pair (tests, fixed
/// emulator identities). Mirrors the pluggable-store pattern of the master data
/// (<see href="../adr/0011-server-stammdatenverwaltung.md">ADR-0011</see>).
/// </remarks>
public interface IServerBankKeyStore
{
    /// <summary>
    /// Returns the bank key pair for <paramref name="host"/>, generating and caching a fresh pair on
    /// first access so that repeated HPB calls for the same host return the same public keys.
    /// </summary>
    /// <param name="host">The host id whose bank key pair to obtain.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The bank's authentication and encryption key pair for <paramref name="host"/>.</returns>
    Task<BankKeyPair> GetOrCreateAsync(HostId host, CancellationToken ct = default);

    /// <summary>
    /// Stores <paramref name="keys"/> for <paramref name="host"/>, replacing any pair previously held
    /// (idempotent upsert). Lets a caller seed a known bank key pair instead of the auto-generated one.
    /// </summary>
    /// <param name="host">The host id the key pair belongs to.</param>
    /// <param name="keys">The bank key pair to store.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the pair has been stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    Task SetAsync(HostId host, BankKeyPair keys, CancellationToken ct = default);
}
