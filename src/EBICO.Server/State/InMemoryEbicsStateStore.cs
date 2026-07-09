using System.Collections.Concurrent;
using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// A thread-safe in-memory <see cref="IEbicsStateStore"/>. This is the default registration and
/// the natural choice for the emulator and tests; nothing is persisted across restarts.
/// </summary>
public sealed class InMemoryEbicsStateStore : IEbicsStateStore
{
    private readonly ConcurrentDictionary<HostId, Bank> _banks = new();
    private readonly ConcurrentDictionary<PartnerId, Partner> _partners = new();
    private readonly ConcurrentDictionary<(HostId Host, PartnerId Partner, UserId User), Subscriber> _subscribers = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Bank>>(_banks.Values.ToArray());

    /// <inheritdoc />
    public Task<Bank?> GetBankAsync(HostId hostId, CancellationToken ct = default)
    {
        _banks.TryGetValue(hostId, out var bank);
        return Task.FromResult(bank);
    }

    /// <inheritdoc />
    public Task RegisterBankAsync(Bank bank, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        _banks[bank.HostId] = bank;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Partner>>(_partners.Values.ToArray());

    /// <inheritdoc />
    public Task<Partner?> GetPartnerAsync(PartnerId partnerId, CancellationToken ct = default)
    {
        _partners.TryGetValue(partnerId, out var partner);
        return Task.FromResult(partner);
    }

    /// <inheritdoc />
    public Task RegisterPartnerAsync(Partner partner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(partner);
        _partners[partner.PartnerId] = partner;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Subscriber>>(_subscribers.Values.ToArray());

    /// <inheritdoc />
    public Task<Subscriber?> GetSubscriberAsync(HostId hostId, PartnerId partnerId, UserId userId, CancellationToken ct = default)
    {
        _subscribers.TryGetValue((hostId, partnerId, userId), out var subscriber);
        return Task.FromResult(subscriber);
    }

    /// <inheritdoc />
    public Task RegisterSubscriberAsync(Subscriber subscriber, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        _subscribers[(subscriber.HostId, subscriber.PartnerId, subscriber.UserId)] = subscriber;
        return Task.CompletedTask;
    }
}
