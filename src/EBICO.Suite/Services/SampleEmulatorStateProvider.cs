using EBICO.Core;
using EBICO.Core.Domain;

namespace EBICO.Suite.Services;

/// <summary>
/// In-memory sample implementation of <see cref="IEmulatorStateProvider"/> that serves a small,
/// deterministic set of sample banks, partners and subscribers built from the
/// <see cref="EBICO.Core.Domain"/> aggregates, plus the sample key catalogue built from
/// <see cref="KeyStoreSeedData"/>.
/// </summary>
/// <remarks>
/// This is the sample-data source: <see cref="EmulatorStateSeeder"/> seeds the master data into the
/// server store from here, and <see cref="KeyStoreSeeder"/> seeds the same key material into the
/// server key stores. It lets the UI grundgerüst (#52) demonstrate the state binding end-to-end; the
/// live views bind <see cref="EmulatorStateProvider"/> over the actual stores.
/// </remarks>
public sealed class SampleEmulatorStateProvider : IEmulatorStateProvider
{
    private static readonly IReadOnlyList<Bank> Banks =
    [
        new Bank(HostId.Create("EBICOHOST"), "EBICO Test-Bank"),
        new Bank(HostId.Create("BANKB"), "Zweitbank", [EbicsVersion.H004, EbicsVersion.H005]),
    ];

    private static readonly IReadOnlyList<Partner> Partners =
    [
        new Partner(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), "Muster GmbH"),
        new Partner(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER02"), "Beispiel AG"),
        // Same PartnerID at a different bank — a distinct customer (Mehr-Mandanten-Fähigkeit).
        new Partner(HostId.Create("BANKB"), PartnerId.Create("PARTNER02"), "Zweitbank-Kunde"),
    ];

    private static readonly IReadOnlyList<Subscriber> Subscribers =
    [
        new Subscriber(
            HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER0001"),
            state: SubscriberState.Ready,
            permissions:
            [
                new SubscriberPermission("CCT", SignatureClass.E),
                new SubscriberPermission("STA", SignatureClass.T),
            ]),
        new Subscriber(
            HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER0002"),
            state: SubscriberState.Initialized,
            permissions: [new SubscriberPermission("CCT", SignatureClass.A)]),
        new Subscriber(
            HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER02"), UserId.Create("USER0003"),
            systemId: SystemId.Create("SYS01"),
            state: SubscriberState.New),
        new Subscriber(
            HostId.Create("BANKB"), PartnerId.Create("PARTNER02"), UserId.Create("USER0004"),
            state: SubscriberState.Suspended),
    ];

    // The sample keys, built once from the shared seed data so they are stable across calls and
    // identical (owner/version/fingerprint) to what the live store-backed provider surfaces.
    private static readonly IReadOnlyList<KeyView> Keys = BuildKeys();

    /// <inheritdoc />
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Banks);

    /// <inheritdoc />
    public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Partners);

    /// <inheritdoc />
    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Subscribers);

    /// <inheritdoc />
    public Task<IReadOnlyList<KeyView>> GetKeysAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Keys);

    private static IReadOnlyList<KeyView> BuildKeys()
    {
        var keys = new List<KeyView>();
        foreach (var (subscriber, key) in KeyStoreSeedData.SubscriberKeys)
        {
            keys.Add(KeyViewFactory.Create(
                $"Teilnehmer {subscriber.PartnerId.Value} / {subscriber.UserId.Value}", key.Version, key.Key));
        }

        foreach (var (host, pair) in KeyStoreSeedData.BankKeys)
        {
            keys.Add(KeyViewFactory.Create($"Bank {host.Value}", pair.AuthenticationVersion, pair.Authentication));
            keys.Add(KeyViewFactory.Create($"Bank {host.Value}", pair.EncryptionVersion, pair.Encryption));
        }

        return keys;
    }
}
