using EBICO.Core;
using EBICO.Core.Domain;

namespace EBICO.Suite.Services;

/// <summary>
/// In-memory placeholder implementation of <see cref="IEmulatorStateProvider"/> that
/// serves a small, deterministic set of sample banks, partners and subscribers built
/// from the <see cref="EBICO.Core.Domain"/> aggregates.
/// </summary>
/// <remarks>
/// Placeholder until the real emulator store exists (server layer, M3/M4). It lets the
/// UI grundgerüst (#52) demonstrate the state binding end-to-end; once the server store
/// is available this implementation is swapped for one reading the live state.
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

    /// <inheritdoc />
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Banks);

    /// <inheritdoc />
    public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Partners);

    /// <inheritdoc />
    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Subscribers);
}
