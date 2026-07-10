using EBICO.Core.Domain;

namespace EBICO.Suite.Services;

/// <summary>
/// Read-only access to the EBICS emulator's server-side state for the Suite UI:
/// the registered banks, partners (Kunden) and subscribers (Teilnehmer).
/// </summary>
/// <remarks>
/// This is the Suite's read-model contract. The real emulator store (keys,
/// transactions, onboarding state) is built in the server layer (M3/M4); until then
/// the UI is wired against <see cref="SampleEmulatorStateProvider"/>. The methods are
/// asynchronous on purpose so that a later backend (in-process store or HTTP API,
/// see ADR-0009) can be plugged in without changing the call sites.
/// </remarks>
public interface IEmulatorStateProvider
{
    /// <summary>Returns the banks (EBICS host endpoints) known to the emulator.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The registered <see cref="Bank"/>s.</returns>
    Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the partners (Kunden) registered at the emulator.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The registered <see cref="Partner"/>s.</returns>
    Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the subscribers (Teilnehmer) registered at the emulator.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The registered <see cref="Subscriber"/>s.</returns>
    Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the public keys (subscriber and bank signature/encryption/authentication keys)
    /// known to the emulator, each with its precomputed SHA-256 fingerprint, for the key/certificate
    /// view (issue #55).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The known <see cref="KeyView"/>s.</returns>
    Task<IReadOnlyList<KeyView>> GetKeysAsync(CancellationToken cancellationToken = default);
}
