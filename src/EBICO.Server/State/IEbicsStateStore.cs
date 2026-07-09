using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// The authoritative server-side state of the EBICS emulator: the registered banks, partners
/// (Kunden) and subscribers (Teilnehmer). This is the read/write counterpart to the Suite's
/// read-only <c>IEmulatorStateProvider</c>.
/// </summary>
/// <remarks>
/// Pluggable via DI; the default registration is the in-memory <see cref="InMemoryEbicsStateStore"/>.
/// The methods are asynchronous so a persistent store can be plugged in later without changing
/// call sites. Key material, transactions and onboarding state are added by later M3/M4 issues;
/// the unification with the Suite read-model (in-process or HTTP API, see ADR-0009) is M4.
/// </remarks>
public interface IEbicsStateStore
{
    /// <summary>Returns all registered banks.</summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The registered <see cref="Bank"/>s.</returns>
    Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken ct = default);

    /// <summary>Returns the bank with the given <paramref name="hostId"/>, or <see langword="null"/>.</summary>
    /// <param name="hostId">The bank's host identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The bank, or <see langword="null"/> when none is registered for the id.</returns>
    Task<Bank?> GetBankAsync(HostId hostId, CancellationToken ct = default);

    /// <summary>Registers (or replaces) a bank, keyed by its <see cref="Bank.HostId"/>.</summary>
    /// <param name="bank">The bank to register.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the bank has been stored.</returns>
    Task RegisterBankAsync(Bank bank, CancellationToken ct = default);

    /// <summary>Returns all registered partners.</summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The registered <see cref="Partner"/>s.</returns>
    Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken ct = default);

    /// <summary>Returns the partner with the given <paramref name="partnerId"/>, or <see langword="null"/>.</summary>
    /// <param name="partnerId">The partner's identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The partner, or <see langword="null"/> when none is registered for the id.</returns>
    Task<Partner?> GetPartnerAsync(PartnerId partnerId, CancellationToken ct = default);

    /// <summary>Registers (or replaces) a partner, keyed by its <see cref="Partner.PartnerId"/>.</summary>
    /// <param name="partner">The partner to register.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the partner has been stored.</returns>
    Task RegisterPartnerAsync(Partner partner, CancellationToken ct = default);

    /// <summary>Returns all registered subscribers.</summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The registered <see cref="Subscriber"/>s.</returns>
    Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the subscriber identified by the (<paramref name="hostId"/>,
    /// <paramref name="partnerId"/>, <paramref name="userId"/>) triple, or <see langword="null"/>.
    /// </summary>
    /// <param name="hostId">The bank's host identifier.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The subscriber, or <see langword="null"/> when none matches the triple.</returns>
    Task<Subscriber?> GetSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, CancellationToken ct = default);

    /// <summary>
    /// Registers (or replaces) a subscriber, keyed by its
    /// (<see cref="Subscriber.HostId"/>, <see cref="Subscriber.PartnerId"/>, <see cref="Subscriber.UserId"/>) triple.
    /// </summary>
    /// <param name="subscriber">The subscriber to register.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the subscriber has been stored.</returns>
    Task RegisterSubscriberAsync(Subscriber subscriber, CancellationToken ct = default);
}
