namespace EBICO.Core.Domain;

/// <summary>
/// An EBICS subscriber (Teilnehmer): a user acting for a partner against a bank,
/// identified by the (<see cref="HostId"/>, <see cref="PartnerId"/>, <see cref="UserId"/>)
/// triple, optionally carrying a <see cref="SystemId"/> for technical / multi-user setups.
/// </summary>
/// <remarks>
/// The aggregate is immutable: lifecycle changes via <see cref="Transition(SubscriberState)"/>
/// return a new instance rather than mutating in place, matching the rest of the Core.
/// Persistence and key material are out of scope here (server layer, M3).
/// </remarks>
public sealed class Subscriber
{
    private readonly SubscriberPermission[] _permissions;

    /// <summary>Creates a subscriber.</summary>
    /// <param name="hostId">The bank's host identifier.</param>
    /// <param name="partnerId">The partner the subscriber belongs to.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="systemId">Optional system identifier for technical / multi-user subscribers.</param>
    /// <param name="state">The initial lifecycle state; defaults to <see cref="SubscriberState.New"/>.</param>
    /// <param name="permissions">Optional order authorisations held by the subscriber.</param>
    public Subscriber(
        HostId hostId,
        PartnerId partnerId,
        UserId userId,
        SystemId? systemId = null,
        SubscriberState state = SubscriberState.New,
        IEnumerable<SubscriberPermission>? permissions = null)
    {
        HostId = hostId;
        PartnerId = partnerId;
        UserId = userId;
        SystemId = systemId;
        State = state;
        _permissions = permissions?.ToArray() ?? [];
    }

    /// <summary>The bank's host identifier (<c>HostID</c>).</summary>
    public HostId HostId { get; }

    /// <summary>The partner identifier (<c>PartnerID</c>) the subscriber belongs to.</summary>
    public PartnerId PartnerId { get; }

    /// <summary>The user identifier (<c>UserID</c>).</summary>
    public UserId UserId { get; }

    /// <summary>The optional system identifier (<c>SystemID</c>) for technical / multi-user subscribers.</summary>
    public SystemId? SystemId { get; }

    /// <summary>The current lifecycle state.</summary>
    public SubscriberState State { get; }

    /// <summary>The order authorisations held by this subscriber.</summary>
    public IReadOnlyCollection<SubscriberPermission> Permissions => _permissions;

    /// <summary>
    /// Indicates whether this is a technical subscriber, i.e. a <see cref="SystemId"/> is
    /// present (multi-user setup).
    /// </summary>
    public bool IsTechnicalSubscriber => SystemId.HasValue;

    /// <summary>
    /// Returns a copy of this subscriber moved to <paramref name="target"/>, validating that
    /// the transition is allowed.
    /// </summary>
    /// <param name="target">The desired lifecycle state.</param>
    /// <returns>A new <see cref="Subscriber"/> in the <paramref name="target"/> state.</returns>
    /// <exception cref="InvalidSubscriberStateTransitionException">
    /// The transition from the current <see cref="State"/> to <paramref name="target"/> is not allowed.
    /// </exception>
    public Subscriber Transition(SubscriberState target)
    {
        if (!IsAllowedTransition(State, target))
        {
            throw new InvalidSubscriberStateTransitionException(
                $"Cannot transition subscriber '{UserId}' from {State} to {target}.");
        }

        return new Subscriber(HostId, PartnerId, UserId, SystemId, target, _permissions);
    }

    /// <summary>
    /// Indicates whether the subscriber may authorise <paramref name="orderType"/>, i.e.
    /// holds a permission for it with a bank-technical signature class (E/A/B).
    /// </summary>
    /// <param name="orderType">The order/BTF type to check.</param>
    /// <returns><see langword="true"/> when an authorising permission exists.</returns>
    public bool CanAuthorize(string orderType) => _permissions.Any(p =>
        string.Equals(p.OrderType, orderType, StringComparison.Ordinal) && p.SignatureClass.IsBankTechnical());

    /// <summary>
    /// Indicates whether the subscriber is restricted to transport for
    /// <paramref name="orderType"/>: at least one permission exists for it and all of them
    /// are transport-only (no authorising signature).
    /// </summary>
    /// <param name="orderType">The order/BTF type to check.</param>
    /// <returns><see langword="true"/> when only transport permissions exist for the order type.</returns>
    public bool IsTransportOnlyFor(string orderType)
    {
        var matching = _permissions
            .Where(p => string.Equals(p.OrderType, orderType, StringComparison.Ordinal))
            .ToArray();

        return matching.Length > 0 && Array.TrueForAll(matching, p => p.SignatureClass.IsTransportOnly());
    }

    /// <summary>
    /// Returns a copy of this subscriber that also holds <paramref name="permission"/>. The
    /// permission set is kept free of duplicates: an identical (<see cref="SubscriberPermission.OrderType"/>,
    /// <see cref="SubscriberPermission.SignatureClass"/>) pair is not added twice.
    /// </summary>
    /// <param name="permission">The permission to grant.</param>
    /// <returns>A new <see cref="Subscriber"/> carrying <paramref name="permission"/>.</returns>
    public Subscriber WithPermission(SubscriberPermission permission)
    {
        if (Array.Exists(_permissions, p => p == permission))
        {
            return this;
        }

        return new Subscriber(HostId, PartnerId, UserId, SystemId, State, [.. _permissions, permission]);
    }

    /// <summary>
    /// Returns a copy of this subscriber with every permission for <paramref name="orderType"/>
    /// removed. When no permission matches, the same instance is returned unchanged.
    /// </summary>
    /// <param name="orderType">The order/BTF type whose permissions are revoked.</param>
    /// <returns>A new <see cref="Subscriber"/> without any permission for the order type.</returns>
    /// <exception cref="ArgumentException"><paramref name="orderType"/> is null, empty or whitespace.</exception>
    public Subscriber WithoutPermissionsFor(string orderType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderType);

        var remaining = Array.FindAll(
            _permissions,
            p => !string.Equals(p.OrderType, orderType, StringComparison.Ordinal));

        return remaining.Length == _permissions.Length
            ? this
            : new Subscriber(HostId, PartnerId, UserId, SystemId, State, remaining);
    }

    /// <summary>
    /// Returns a copy of this subscriber whose permission set is replaced by
    /// <paramref name="permissions"/>. Duplicate (order type, signature class) pairs are collapsed.
    /// </summary>
    /// <param name="permissions">The complete new set of permissions.</param>
    /// <returns>A new <see cref="Subscriber"/> holding exactly <paramref name="permissions"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="permissions"/> is <see langword="null"/>.</exception>
    public Subscriber WithPermissions(IEnumerable<SubscriberPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        return new Subscriber(HostId, PartnerId, UserId, SystemId, State, permissions.Distinct());
    }

    private static bool IsAllowedTransition(SubscriberState from, SubscriberState to) => (from, to) switch
    {
        (SubscriberState.New, SubscriberState.Initialized) => true,
        (SubscriberState.Initialized, SubscriberState.Ready) => true,
        (SubscriberState.New, SubscriberState.Suspended) => true,
        (SubscriberState.Initialized, SubscriberState.Suspended) => true,
        (SubscriberState.Ready, SubscriberState.Suspended) => true,
        (SubscriberState.Suspended, SubscriberState.Ready) => true,
        _ => false,
    };
}
