using EBICO.Core.Administrative;
using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>The outcome of attempting to add a signature to an open VEU order (issue #42).</summary>
public enum VeuSignStatus
{
    /// <summary>The signature was added.</summary>
    Signed,

    /// <summary>No open order matched the (host, partner, order id).</summary>
    NotFound,

    /// <summary>The signing user has already signed this order.</summary>
    DuplicateSigner,

    /// <summary>The order had already reached the required number of signatures.</summary>
    AlreadyComplete,
}

/// <summary>The result of a sign attempt: the outcome plus the affected order when one was found.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Order">The affected order (present for <see cref="VeuSignStatus.Signed"/>/<see cref="VeuSignStatus.DuplicateSigner"/>/<see cref="VeuSignStatus.AlreadyComplete"/>), else <see langword="null"/>.</param>
public readonly record struct VeuSignOutcome(VeuSignStatus Status, OpenVeuOrder? Order);

/// <summary>
/// Holds the orders awaiting distributed signatures (EDS / VEU, issue #42). Unlike the transient
/// transaction stores, entries are partner-scoped and long-lived: an order stays until it is fully signed
/// (then removed on release) or cancelled (HVS). Keyed by (<see cref="HostId"/>, <see cref="PartnerId"/>,
/// order id); the order id is assigned by the store on <see cref="AddAsync"/>.
/// </summary>
/// <remarks>
/// The default registration is the in-memory <see cref="InMemoryOpenVeuStore"/>; a real backing store can be
/// substituted via <c>TryAddSingleton</c> before <c>AddEbicoServer</c>. Nothing is persisted across restarts.
/// </remarks>
public interface IOpenVeuStore
{
    /// <summary>
    /// Adds <paramref name="order"/> to the store, assigning it a fresh order id (set on the instance), and
    /// returns it.
    /// </summary>
    /// <param name="order">The order to park (its <see cref="OpenVeuOrder.OrderId"/> is assigned here).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The same <paramref name="order"/> with its assigned order id.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is <see langword="null"/>.</exception>
    Task<OpenVeuOrder> AddAsync(OpenVeuOrder order, CancellationToken ct = default);

    /// <summary>Returns the open order for the (host, partner, order id), or <see langword="null"/> when none matches.</summary>
    /// <param name="hostId">The bank/host.</param>
    /// <param name="partnerId">The partner (customer).</param>
    /// <param name="orderId">The order id.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The matching order, or <see langword="null"/>.</returns>
    Task<OpenVeuOrder?> TryGetAsync(HostId hostId, PartnerId partnerId, string orderId, CancellationToken ct = default);

    /// <summary>Returns all open orders of a partner (ordered by creation, oldest first).</summary>
    /// <param name="hostId">The bank/host.</param>
    /// <param name="partnerId">The partner (customer) whose open orders to list.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The partner's open orders (possibly empty).</returns>
    Task<IReadOnlyList<OpenVeuOrder>> ListAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default);

    /// <summary>
    /// Adds a signature to the identified order unless the signer already signed it or it is already complete.
    /// </summary>
    /// <param name="hostId">The bank/host.</param>
    /// <param name="partnerId">The partner (customer).</param>
    /// <param name="orderId">The order id.</param>
    /// <param name="signer">The signing subscriber and the signature class used.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The sign outcome and the affected order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signer"/> is <see langword="null"/>.</exception>
    Task<VeuSignOutcome> TrySignAsync(HostId hostId, PartnerId partnerId, string orderId, VeuSignerView signer, CancellationToken ct = default);

    /// <summary>Removes the identified order (on release or cancellation).</summary>
    /// <param name="hostId">The bank/host.</param>
    /// <param name="partnerId">The partner (customer).</param>
    /// <param name="orderId">The order id.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when an order was removed; otherwise <see langword="false"/>.</returns>
    Task<bool> RemoveAsync(HostId hostId, PartnerId partnerId, string orderId, CancellationToken ct = default);
}
