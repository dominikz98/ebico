using EBICO.Core.Crypto;
using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// Identifies the subscriber (Teilnehmer) a stored public key belongs to: the
/// (<see cref="HostId"/>, <see cref="PartnerId"/>, <see cref="UserId"/>) triple that also keys the
/// <see cref="IEbicsStateStore"/> subscribers.
/// </summary>
/// <param name="HostId">The bank's host identifier.</param>
/// <param name="PartnerId">The partner the subscriber belongs to.</param>
/// <param name="UserId">The user identifier.</param>
public readonly record struct SubscriberKeyRef(HostId HostId, PartnerId PartnerId, UserId UserId);

/// <summary>
/// A public key held by the server for a subscriber, together with the EBICS key version it was
/// submitted under (e.g. <c>A005</c> for a signature key). Only the public part is ever stored —
/// the server never sees subscriber private keys.
/// </summary>
/// <param name="Key">The (public-only) RSA key material.</param>
/// <param name="Version">The EBICS key version (e.g. <c>A005</c>/<c>A006</c>) the key was submitted under.</param>
public sealed record StoredPublicKey(RsaKeyMaterial Key, KeyVersion Version)
{
    /// <summary>The role this key plays, derived from <see cref="Version"/> (signature/encryption/authentication).</summary>
    public KeyPurpose Purpose => Version.Purpose;
}

/// <summary>
/// Stores the subscriber public keys the server receives during onboarding, keyed by subscriber and
/// <see cref="KeyPurpose"/>. INI (#26) stores the bank-technical signature key (<c>A00x</c>); HIA
/// (#27) will add the authentication (<c>X00x</c>) and encryption (<c>E00x</c>) keys through the same
/// abstraction. Mirrors the client-side <c>EBICO.Connector.Keys.IKeyStore</c>.
/// </summary>
/// <remarks>
/// This is the server counterpart the <see cref="IEbicsStateStore"/> deliberately left out (its
/// aggregates carry no key material). The default registration is <see cref="InMemoryServerKeyStore"/>;
/// a persistent implementation can be substituted via <c>TryAddSingleton</c> before <c>AddEbicoServer</c>.
/// </remarks>
public interface IServerKeyStore
{
    /// <summary>
    /// Stores <paramref name="key"/> for <paramref name="subscriber"/>, replacing any key previously
    /// held for the same subscriber and <see cref="StoredPublicKey.Purpose"/> (idempotent upsert).
    /// </summary>
    /// <param name="subscriber">The subscriber the key belongs to.</param>
    /// <param name="key">The public key and its EBICS version to store.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the key has been stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    Task StoreAsync(SubscriberKeyRef subscriber, StoredPublicKey key, CancellationToken ct = default);

    /// <summary>Returns the key held for <paramref name="subscriber"/> and <paramref name="purpose"/>, or <see langword="null"/>.</summary>
    /// <param name="subscriber">The subscriber whose key to retrieve.</param>
    /// <param name="purpose">The key purpose (signature/encryption/authentication).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The stored key, or <see langword="null"/> when none is held.</returns>
    Task<StoredPublicKey?> GetAsync(SubscriberKeyRef subscriber, KeyPurpose purpose, CancellationToken ct = default);

    /// <summary>Indicates whether a key is held for <paramref name="subscriber"/> and <paramref name="purpose"/>.</summary>
    /// <param name="subscriber">The subscriber to check.</param>
    /// <param name="purpose">The key purpose (signature/encryption/authentication).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a key is held; otherwise <see langword="false"/>.</returns>
    Task<bool> ContainsAsync(SubscriberKeyRef subscriber, KeyPurpose purpose, CancellationToken ct = default);
}
