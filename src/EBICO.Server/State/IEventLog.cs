namespace EBICO.Server.State;

/// <summary>
/// The shared, append-only event/protocol store of the EBICS emulator (issue #69). Every server
/// component writes relevant events here (a request answered, a transaction lifecycle step, a
/// key-management action, …); nothing is ever mutated or removed by a writer. Two projections read from
/// it without producing their own log: the customer-facing HAC protocol (M5) and the operator-facing
/// Suite inspector (M7).
/// </summary>
/// <remarks>
/// <para>
/// Pluggable via DI exactly like the rest of the server state; the default registration is the in-memory
/// <see cref="InMemoryEventLog"/>. The methods are asynchronous so a persistent store (e.g. SQLite) can
/// be plugged in later via a <c>TryAddSingleton</c> override without changing any call site — the same
/// "in-memory default, pluggable" approach as <see cref="IEbicsStateStore"/> (ADR-0011). A concrete
/// persistent event log is a follow-up.
/// </para>
/// <para>
/// The log owns ordering and time: <see cref="AppendAsync"/> assigns <see cref="EbicsEvent.Sequence"/>
/// (monotonic) and stamps <see cref="EbicsEvent.Timestamp"/>, so a writer supplies only the semantic
/// content of the event.
/// </para>
/// </remarks>
public interface IEventLog
{
    /// <summary>
    /// Appends <paramref name="evt"/> to the log. The log assigns a fresh monotonic
    /// <see cref="EbicsEvent.Sequence"/> and stamps <see cref="EbicsEvent.Timestamp"/> from its clock;
    /// any values the caller set on those two properties are ignored.
    /// </summary>
    /// <param name="evt">The event to append (its semantic content; sequence/timestamp are assigned here).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the event has been stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="evt"/> is <see langword="null"/>.</exception>
    Task AppendAsync(EbicsEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Returns the stored events matching <paramref name="query"/>, ordered by
    /// <see cref="EbicsEvent.Sequence"/> ascending.
    /// </summary>
    /// <param name="query">The filter to apply (every field optional; see <see cref="EbicsEventQuery"/>).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The matching events (possibly empty), oldest first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyList<EbicsEvent>> QueryAsync(EbicsEventQuery query, CancellationToken ct = default);
}
