using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Suite.Services;

/// <summary>
/// Live <see cref="IEmulatorStateProvider"/> bridge over the server-side emulator state: banks,
/// partners and subscribers are read from the in-process <see cref="IEbicsStateStore"/> (the
/// read/write store managed by the <see cref="IMasterDataManager"/>, issue #30), and the public keys
/// from the server key stores — subscriber keys from <see cref="IServerKeyStore"/> (submitted via
/// INI/HIA) and the bank key pair from <see cref="IServerBankKeyStore"/> (returned via HPB). So the UI
/// reflects the actual emulator state rather than static sample data.
/// </summary>
/// <remarks>
/// This realises the ADR-0009 decision to access the server state in-process via DI instead of a
/// dedicated HTTP API. The Suite runs no live EBICS pipeline, so the stores are seeded on start-up
/// (<see cref="EmulatorStateSeeder"/> / <see cref="KeyStoreSeeder"/>) with deterministic sample data.
/// </remarks>
public sealed class EmulatorStateProvider : IEmulatorStateProvider
{
    private static readonly KeyPurpose[] KeyPurposes =
        [KeyPurpose.Signature, KeyPurpose.Encryption, KeyPurpose.Authentication];

    private readonly IEbicsStateStore _store;
    private readonly IServerKeyStore _keyStore;
    private readonly IServerBankKeyStore _bankKeyStore;

    /// <summary>Creates the bridge over the given stores.</summary>
    /// <param name="store">The authoritative server-side state store (banks/partners/subscribers).</param>
    /// <param name="keyStore">The store of subscriber public keys received during onboarding.</param>
    /// <param name="bankKeyStore">The store of the bank's own authentication/encryption key pair.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public EmulatorStateProvider(IEbicsStateStore store, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(bankKeyStore);
        _store = store;
        _keyStore = keyStore;
        _bankKeyStore = bankKeyStore;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default)
        => _store.GetBanksAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default)
        => _store.GetPartnersAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
        => _store.GetSubscribersAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<KeyView>> GetKeysAsync(CancellationToken cancellationToken = default)
    {
        var views = new List<KeyView>();

        // Subscriber keys: the store has no enumerate, so probe each purpose per known subscriber via
        // the non-mutating GetAsync and emit a view for every key actually held.
        var subscribers = await _store.GetSubscribersAsync(cancellationToken).ConfigureAwait(false);
        foreach (var subscriber in subscribers)
        {
            var subscriberRef = new SubscriberKeyRef(subscriber.HostId, subscriber.PartnerId, subscriber.UserId);
            foreach (var purpose in KeyPurposes)
            {
                var stored = await _keyStore.GetAsync(subscriberRef, purpose, cancellationToken).ConfigureAwait(false);
                if (stored is not null)
                {
                    views.Add(KeyViewFactory.Create(
                        $"Teilnehmer {subscriber.PartnerId.Value} / {subscriber.UserId.Value}",
                        stored.Version,
                        stored.Key));
                }
            }
        }

        // Bank keys: read only the seeded hosts, so GetOrCreateAsync always hits the cached seeded pair
        // and never generates a fresh (non-deterministic) pair as a side effect of rendering the view.
        foreach (var host in KeyStoreSeedData.BankHosts)
        {
            var pair = await _bankKeyStore.GetOrCreateAsync(host, cancellationToken).ConfigureAwait(false);
            views.Add(KeyViewFactory.Create($"Bank {host.Value}", pair.AuthenticationVersion, pair.Authentication));
            views.Add(KeyViewFactory.Create($"Bank {host.Value}", pair.EncryptionVersion, pair.Encryption));
        }

        return views;
    }
}
