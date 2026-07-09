using EBICO.Core.Crypto;

namespace EBICO.Connector.Keys;

/// <summary>Identifies which party a stored key belongs to.</summary>
public enum KeyOwner
{
    /// <summary>The subscriber's own key pair (private key present). Used for A00x/E002/X002.</summary>
    Subscriber,

    /// <summary>A public bank key obtained during onboarding (HPB). Used for X002/E002.</summary>
    Bank,
}

/// <summary>
/// Abstraction over the store that holds the key material exchanged during EBICS onboarding: the
/// subscriber's own key pairs and the bank's public keys. Implementations range from in-memory
/// (tests) through a simple file store to an HSM-backed store (later issues).
/// </summary>
/// <remarks>
/// In this connector-core skeleton the store is implicitly scoped to the single configured
/// subscriber. Multi-subscriber scoping and key-version resolution are later concerns. Keys are
/// identified by <see cref="KeyOwner"/> and <see cref="KeyPurpose"/>; the concrete key version
/// (e.g. A005 vs A006) is resolved via the <c>EBICO.Core.Crypto.KeyVersions</c> registry elsewhere.
/// </remarks>
public interface IKeyStore
{
    /// <summary>Retrieves the key material for the given owner and purpose.</summary>
    /// <param name="owner">Whose key to retrieve.</param>
    /// <param name="purpose">The cryptographic purpose (signature, encryption, authentication).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The key material, or <see langword="null"/> when no such key is stored.</returns>
    Task<RsaKeyMaterial?> GetAsync(KeyOwner owner, KeyPurpose purpose, CancellationToken ct = default);

    /// <summary>Stores (or replaces) the key material for the given owner and purpose.</summary>
    /// <param name="owner">Whose key to store.</param>
    /// <param name="purpose">The cryptographic purpose.</param>
    /// <param name="material">The key material to store.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the key has been stored.</returns>
    Task StoreAsync(KeyOwner owner, KeyPurpose purpose, RsaKeyMaterial material, CancellationToken ct = default);

    /// <summary>Reports whether a key for the given owner and purpose is present.</summary>
    /// <param name="owner">Whose key to check.</param>
    /// <param name="purpose">The cryptographic purpose.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the key is stored.</returns>
    Task<bool> ContainsAsync(KeyOwner owner, KeyPurpose purpose, CancellationToken ct = default);
}
