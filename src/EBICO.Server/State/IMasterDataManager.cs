using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// The master-data management layer of the emulator (Stammdatenverwaltung): full CRUD over the
/// banks, partners (Kunden) and subscribers (Teilnehmer) held by the <see cref="IEbicsStateStore"/>,
/// enforcing referential integrity, cascading deletes and the subscriber permission/lifecycle rules.
/// </summary>
/// <remarks>
/// <para>
/// This sits on top of the low-level <see cref="IEbicsStateStore"/>: the store persists aggregates
/// by identity, this manager adds the business rules. Creating a partner requires its bank to exist;
/// creating a subscriber requires its bank <em>and</em> partner to exist. Deleting a bank cascades
/// to its partners and subscribers; deleting a partner cascades to its subscribers.
/// </para>
/// <para>
/// The model is multi-tenant: banks are identified by <c>HostID</c>, partners by the
/// (<c>HostID</c>, <c>PartnerID</c>) pair and subscribers by the (<c>HostID</c>, <c>PartnerID</c>,
/// <c>UserID</c>) triple, so the same partner or user id can exist independently at different banks.
/// </para>
/// </remarks>
public interface IMasterDataManager
{
    // --- Banks -----------------------------------------------------------------------------

    /// <summary>Returns all registered banks.</summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The registered <see cref="Bank"/>s.</returns>
    Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken ct = default);

    /// <summary>Returns the bank with the given <paramref name="hostId"/>, or <see langword="null"/>.</summary>
    /// <param name="hostId">The bank's host identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The bank, or <see langword="null"/> when none is registered.</returns>
    Task<Bank?> GetBankAsync(HostId hostId, CancellationToken ct = default);

    /// <summary>Creates or updates <paramref name="bank"/> (idempotent upsert keyed by host id).</summary>
    /// <param name="bank">The bank to store.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The stored bank.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bank"/> is <see langword="null"/>.</exception>
    Task<Bank> SaveBankAsync(Bank bank, CancellationToken ct = default);

    /// <summary>
    /// Deletes the bank <paramref name="hostId"/> and cascades to every partner and subscriber
    /// belonging to it.
    /// </summary>
    /// <param name="hostId">The host identifier of the bank to delete.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the bank existed and was deleted; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteBankAsync(HostId hostId, CancellationToken ct = default);

    // --- Partners --------------------------------------------------------------------------

    /// <summary>Returns all partners registered for the bank <paramref name="hostId"/>.</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The partners belonging to the bank (possibly empty).</returns>
    Task<IReadOnlyList<Partner>> GetPartnersAsync(HostId hostId, CancellationToken ct = default);

    /// <summary>Returns the partner (<paramref name="hostId"/>, <paramref name="partnerId"/>), or <see langword="null"/>.</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner's identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The partner, or <see langword="null"/> when none matches.</returns>
    Task<Partner?> GetPartnerAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default);

    /// <summary>Creates or updates <paramref name="partner"/> (idempotent upsert).</summary>
    /// <param name="partner">The partner to store.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The stored partner.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="partner"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnknownBankException">No bank is registered for <see cref="Partner.HostId"/>.</exception>
    Task<Partner> SavePartnerAsync(Partner partner, CancellationToken ct = default);

    /// <summary>
    /// Deletes the partner (<paramref name="hostId"/>, <paramref name="partnerId"/>) and cascades to
    /// every subscriber belonging to it.
    /// </summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner's identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the partner existed and was deleted; otherwise <see langword="false"/>.</returns>
    Task<bool> DeletePartnerAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default);

    // --- Subscribers -----------------------------------------------------------------------

    /// <summary>Returns all subscribers registered for the partner (<paramref name="hostId"/>, <paramref name="partnerId"/>).</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner the subscribers belong to.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The subscribers belonging to the partner (possibly empty).</returns>
    Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default);

    /// <summary>Returns the subscriber (<paramref name="hostId"/>, <paramref name="partnerId"/>, <paramref name="userId"/>), or <see langword="null"/>.</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The subscriber, or <see langword="null"/> when none matches.</returns>
    Task<Subscriber?> GetSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, CancellationToken ct = default);

    /// <summary>Creates or updates <paramref name="subscriber"/> (idempotent upsert).</summary>
    /// <param name="subscriber">The subscriber to store.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The stored subscriber.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="subscriber"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnknownBankException">No bank is registered for the subscriber's host id.</exception>
    /// <exception cref="UnknownPartnerException">No partner is registered for the subscriber's (host, partner) pair.</exception>
    Task<Subscriber> SaveSubscriberAsync(Subscriber subscriber, CancellationToken ct = default);

    /// <summary>Deletes the subscriber (<paramref name="hostId"/>, <paramref name="partnerId"/>, <paramref name="userId"/>).</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the subscriber existed and was deleted; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, CancellationToken ct = default);

    /// <summary>Moves the subscriber to <paramref name="target"/>, validating the lifecycle transition.</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="target">The desired lifecycle state.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The updated subscriber.</returns>
    /// <exception cref="UnknownSubscriberException">The subscriber does not exist.</exception>
    /// <exception cref="InvalidSubscriberStateTransitionException">The transition is not allowed.</exception>
    Task<Subscriber> TransitionSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, SubscriberState target, CancellationToken ct = default);

    /// <summary>Replaces the subscriber's permission set with <paramref name="permissions"/> (per OrderType/BTF).</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="permissions">The complete new set of permissions.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The updated subscriber.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="permissions"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnknownSubscriberException">The subscriber does not exist.</exception>
    Task<Subscriber> SetPermissionsAsync(HostId hostId, PartnerId partnerId, UserId userId, IEnumerable<SubscriberPermission> permissions, CancellationToken ct = default);

    /// <summary>Grants a single permission (OrderType × SignatureClass) to the subscriber.</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="permission">The permission to grant.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The updated subscriber.</returns>
    /// <exception cref="UnknownSubscriberException">The subscriber does not exist.</exception>
    Task<Subscriber> GrantPermissionAsync(HostId hostId, PartnerId partnerId, UserId userId, SubscriberPermission permission, CancellationToken ct = default);

    /// <summary>Revokes every permission held for <paramref name="orderType"/> from the subscriber.</summary>
    /// <param name="hostId">The host identifier of the bank.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="orderType">The order/BTF type whose permissions are revoked.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The updated subscriber.</returns>
    /// <exception cref="ArgumentException"><paramref name="orderType"/> is null, empty or whitespace.</exception>
    /// <exception cref="UnknownSubscriberException">The subscriber does not exist.</exception>
    Task<Subscriber> RevokePermissionsAsync(HostId hostId, PartnerId partnerId, UserId userId, string orderType, CancellationToken ct = default);
}
