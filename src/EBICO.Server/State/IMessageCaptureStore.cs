namespace EBICO.Server.State;

/// <summary>
/// Append-only store of raw request/response XML captures (issue #54), keyed by the upper-case hex
/// transaction id (<see cref="CapturedMessage.TransactionIdHex"/>). The request pipeline appends one
/// capture per processed transaction message; the Suite transaction inspector reads them back per
/// transaction to show the raw wire XML of each phase.
/// </summary>
/// <remarks>
/// This is the transaction-scoped, raw-XML counterpart to the semantic <see cref="IEventLog"/>: the event
/// log records <em>what</em> happened (structured, feeding HAC and the inspector), the capture store keeps
/// the verbatim envelopes for the inspector only. The default registration is the thread-safe in-memory
/// <see cref="InMemoryMessageCaptureStore"/>; the interface is asynchronous so a persistent store (SQLite
/// or similar) can be substituted via <c>TryAddSingleton&lt;IMessageCaptureStore, …&gt;</c> before
/// <c>AddEbicoServer</c> without changing a caller (ADR-0011/ADR-0015).
/// </remarks>
public interface IMessageCaptureStore
{
    /// <summary>
    /// Appends a captured message. The store stamps <see cref="CapturedMessage.Sequence"/> and
    /// <see cref="CapturedMessage.Timestamp"/> and may truncate the XML for storage; a writer supplies only
    /// the semantic content.
    /// </summary>
    /// <param name="message">The message to capture.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the message has been stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    Task AppendAsync(CapturedMessage message, CancellationToken ct = default);

    /// <summary>
    /// Returns the captured messages of one transaction, ordered by <see cref="CapturedMessage.Sequence"/>
    /// ascending. An unknown transaction id yields an empty list.
    /// </summary>
    /// <param name="transactionIdHex">The upper-case hex form of the transaction id.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The captures for the transaction (empty when none).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transactionIdHex"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyList<CapturedMessage>> GetAsync(string transactionIdHex, CancellationToken ct = default);
}
