using EBICO.Core.Administrative;
using EBICO.Core.Domain;

namespace EBICO.Server.State;

/// <summary>
/// A thread-safe in-memory <see cref="IOpenVeuStore"/>: the orders awaiting distributed signatures (VEU,
/// issue #42), keyed by (<see cref="HostId"/>, <see cref="PartnerId"/>, order id). Order ids are assigned
/// from a monotonic counter formatted as a 4-character EBICS order id (a leading <c>V</c> plus a base-36
/// sequence). The default registration and the natural choice for the emulator and tests; nothing is
/// persisted across restarts.
/// </summary>
public sealed class InMemoryOpenVeuStore : IOpenVeuStore
{
    private const string Base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private readonly object _gate = new();
    private readonly Dictionary<(HostId Host, PartnerId Partner, string OrderId), OpenVeuOrder> _orders = [];
    private long _counter;

    /// <inheritdoc />
    public Task<OpenVeuOrder> AddAsync(OpenVeuOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        lock (_gate)
        {
            order.OrderId = NextOrderId();
            _orders[(order.HostId, order.PartnerId, order.OrderId)] = order;
        }

        return Task.FromResult(order);
    }

    /// <inheritdoc />
    public Task<OpenVeuOrder?> TryGetAsync(HostId hostId, PartnerId partnerId, string orderId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _orders.TryGetValue((hostId, partnerId, orderId), out var order);
            return Task.FromResult(order);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OpenVeuOrder>> ListAsync(HostId hostId, PartnerId partnerId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<OpenVeuOrder> result = _orders.Values
                .Where(o => o.HostId == hostId && o.PartnerId == partnerId)
                .OrderBy(o => o.CreatedAt)
                .ThenBy(o => o.OrderId, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<VeuSignOutcome> TrySignAsync(HostId hostId, PartnerId partnerId, string orderId, VeuSignerView signer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signer);

        lock (_gate)
        {
            if (!_orders.TryGetValue((hostId, partnerId, orderId), out var order))
            {
                return Task.FromResult(new VeuSignOutcome(VeuSignStatus.NotFound, null));
            }

            if (order.IsFullySigned)
            {
                return Task.FromResult(new VeuSignOutcome(VeuSignStatus.AlreadyComplete, order));
            }

            if (UserId.TryCreate(signer.UserId, out var userId) && order.HasSignerFor(userId))
            {
                return Task.FromResult(new VeuSignOutcome(VeuSignStatus.DuplicateSigner, order));
            }

            order.AddSignature(signer);
            return Task.FromResult(new VeuSignOutcome(VeuSignStatus.Signed, order));
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(HostId hostId, PartnerId partnerId, string orderId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_orders.Remove((hostId, partnerId, orderId)));
        }
    }

    // Formats the next monotonic counter as a 4-character EBICS order id: a leading 'V' (a letter, as the
    // first character must be [A-Z]) plus a 3-digit base-36 sequence (36^3 = 46656 ids before it wraps).
    // Callers hold _gate.
    private string NextOrderId()
    {
        var n = (int)(_counter++ % (36 * 36 * 36));
        Span<char> chars = ['V', Base36[n / (36 * 36)], Base36[n / 36 % 36], Base36[n % 36]];
        return new string(chars);
    }
}
