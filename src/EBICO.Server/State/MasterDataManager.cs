using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// Default <see cref="IMasterDataManager"/> built on top of an <see cref="IEbicsStateStore"/>.
/// It enforces the referential-integrity and cascading rules while delegating persistence to the
/// (pluggable) store.
/// </summary>
public sealed class MasterDataManager : IMasterDataManager
{
    private readonly IEbicsStateStore _store;

    /// <summary>Creates the manager over the given <paramref name="store"/>.</summary>
    /// <param name="store">The backing state store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public MasterDataManager(IEbicsStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    // --- Banks -----------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken ct = default)
        => _store.GetBanksAsync(ct);

    /// <inheritdoc />
    public Task<Bank?> GetBankAsync(HostId hostId, CancellationToken ct = default)
        => _store.GetBankAsync(hostId, ct);

    /// <inheritdoc />
    public async Task<Bank> SaveBankAsync(Bank bank, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        await _store.RegisterBankAsync(bank, ct).ConfigureAwait(false);
        return bank;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteBankAsync(HostId hostId, CancellationToken ct = default)
    {
        // Cascade: remove every subscriber and partner of the host first, then the bank itself.
        foreach (var subscriber in await _store.GetSubscribersForBankAsync(hostId, ct).ConfigureAwait(false))
        {
            await _store.RemoveSubscriberAsync(subscriber.HostId, subscriber.PartnerId, subscriber.UserId, ct)
                .ConfigureAwait(false);
        }

        foreach (var partner in await _store.GetPartnersForBankAsync(hostId, ct).ConfigureAwait(false))
        {
            await _store.RemovePartnerAsync(partner.HostId, partner.PartnerId, ct).ConfigureAwait(false);
        }

        return await _store.RemoveBankAsync(hostId, ct).ConfigureAwait(false);
    }

    // --- Partners --------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<Partner>> GetPartnersAsync(HostId hostId, CancellationToken ct = default)
        => _store.GetPartnersForBankAsync(hostId, ct);

    /// <inheritdoc />
    public Task<Partner?> GetPartnerAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default)
        => _store.GetPartnerAsync(hostId, partnerId, ct);

    /// <inheritdoc />
    public async Task<Partner> SavePartnerAsync(Partner partner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(partner);
        await EnsureBankExistsAsync(partner.HostId, ct).ConfigureAwait(false);
        await _store.RegisterPartnerAsync(partner, ct).ConfigureAwait(false);
        return partner;
    }

    /// <inheritdoc />
    public async Task<bool> DeletePartnerAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default)
    {
        // Cascade: remove every subscriber of the partner first, then the partner itself.
        foreach (var subscriber in await _store.GetSubscribersForPartnerAsync(hostId, partnerId, ct).ConfigureAwait(false))
        {
            await _store.RemoveSubscriberAsync(subscriber.HostId, subscriber.PartnerId, subscriber.UserId, ct)
                .ConfigureAwait(false);
        }

        return await _store.RemovePartnerAsync(hostId, partnerId, ct).ConfigureAwait(false);
    }

    // --- Subscribers -----------------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default)
        => _store.GetSubscribersForPartnerAsync(hostId, partnerId, ct);

    /// <inheritdoc />
    public Task<Subscriber?> GetSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, CancellationToken ct = default)
        => _store.GetSubscriberAsync(hostId, partnerId, userId, ct);

    /// <inheritdoc />
    public async Task<Subscriber> SaveSubscriberAsync(Subscriber subscriber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        await EnsureBankExistsAsync(subscriber.HostId, ct).ConfigureAwait(false);
        await EnsurePartnerExistsAsync(subscriber.HostId, subscriber.PartnerId, ct).ConfigureAwait(false);
        await _store.RegisterSubscriberAsync(subscriber, ct).ConfigureAwait(false);
        return subscriber;
    }

    /// <inheritdoc />
    public Task<bool> DeleteSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, CancellationToken ct = default)
        => _store.RemoveSubscriberAsync(hostId, partnerId, userId, ct);

    /// <inheritdoc />
    public async Task<Subscriber> TransitionSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, SubscriberState target, CancellationToken ct = default)
    {
        var subscriber = await RequireSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        var updated = subscriber.Transition(target);
        await _store.RegisterSubscriberAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async Task<Subscriber> SetPermissionsAsync(HostId hostId, PartnerId partnerId, UserId userId, IEnumerable<SubscriberPermission> permissions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var subscriber = await RequireSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        var updated = subscriber.WithPermissions(permissions);
        await _store.RegisterSubscriberAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async Task<Subscriber> GrantPermissionAsync(HostId hostId, PartnerId partnerId, UserId userId, SubscriberPermission permission, CancellationToken ct = default)
    {
        var subscriber = await RequireSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        var updated = subscriber.WithPermission(permission);
        await _store.RegisterSubscriberAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async Task<Subscriber> RevokePermissionsAsync(HostId hostId, PartnerId partnerId, UserId userId, string orderType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderType);
        var subscriber = await RequireSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        var updated = subscriber.WithoutPermissionsFor(orderType);
        await _store.RegisterSubscriberAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    // --- Helpers ---------------------------------------------------------------------------

    private async Task EnsureBankExistsAsync(HostId hostId, CancellationToken ct)
    {
        if (await _store.GetBankAsync(hostId, ct).ConfigureAwait(false) is null)
        {
            throw new UnknownBankException($"No bank is registered for host '{hostId}'.");
        }
    }

    private async Task EnsurePartnerExistsAsync(HostId hostId, PartnerId partnerId, CancellationToken ct)
    {
        if (await _store.GetPartnerAsync(hostId, partnerId, ct).ConfigureAwait(false) is null)
        {
            throw new UnknownPartnerException($"No partner '{partnerId}' is registered for host '{hostId}'.");
        }
    }

    private async Task<Subscriber> RequireSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, CancellationToken ct)
    {
        var subscriber = await _store.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        return subscriber
            ?? throw new UnknownSubscriberException(
                $"No subscriber '{userId}' is registered for partner '{partnerId}' at host '{hostId}'.");
    }
}
