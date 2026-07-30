namespace EBICO.Suite.Services;

/// <summary>
/// Broadcasts "the master data changed" to every component that displays it, so the independently
/// rendered management islands on the master-data page stay consistent with each other (issue #126).
/// </summary>
/// <remarks>
/// <para>
/// The master-data page hosts <c>BankManager</c>, <c>PartnerManager</c> and <c>SubscriberManager</c> as
/// three <em>separate</em> <c>InteractiveServer</c> islands. Each holds its own copy of the state, but
/// all three write through the same <see cref="EBICO.Server.State.IMasterDataManager"/> — and a bank
/// deletion cascades into partners and subscribers. Without a notification path a mutation in one
/// island silently invalidates the other two: a freshly created bank was missing from the partner and
/// subscriber dropdowns, and cascade-deleted rows lingered until a full page reload.
/// </para>
/// <para>
/// The implementation is registered as a <b>singleton</b> so that concurrent browser sessions converge
/// too — the underlying emulator stores are process-wide singletons as well (ADR-0009). Handlers are
/// therefore invoked on the notifying circuit's thread; a subscriber must marshal back onto its own
/// renderer (<c>ComponentBase.InvokeAsync</c>) before touching component state.
/// </para>
/// </remarks>
public interface IMasterDataChangeNotifier
{
    /// <summary>
    /// Registers <paramref name="handler"/> to be invoked on every subsequent change.
    /// </summary>
    /// <param name="handler">The callback to invoke; must marshal onto its own renderer before touching component state.</param>
    /// <returns>A token that removes the subscription when disposed. Components must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    IDisposable Subscribe(Func<Task> handler);

    /// <summary>
    /// Invokes every current subscriber. Call this after a successful mutation of the master data.
    /// </summary>
    /// <returns>A task that completes once every subscriber has been invoked.</returns>
    /// <remarks>
    /// Every handler is invoked even when an earlier one throws; the failures are then surfaced
    /// together as an <see cref="AggregateException"/> rather than cutting the broadcast short.
    /// </remarks>
    /// <exception cref="AggregateException">One or more handlers threw.</exception>
    Task NotifyChangedAsync();
}
