using EBICO.Server.State;

namespace EBICO.Suite.Services;

/// <summary>
/// Read-only access to the emulator's transaction and event state for the Suite transaction inspector
/// (issue #54): the list of upload/download transactions (reconstructed from the event log and enriched
/// from the transaction stores), the detail of one transaction (events, raw XML captures, decrypted order
/// data) and the raw, global event/protocol log with live filters.
/// </summary>
/// <remarks>
/// This is a dedicated read-model contract, separate from <see cref="IEmulatorStateProvider"/> (which is
/// the master-data view): the two concerns evolve independently and no consumer is forced to depend on
/// both. It realises the ADR-0009 decision to read the server-side state in-process via DI rather than a
/// dedicated HTTP API. Unlike the customer-facing HAC projection, the inspector reads the log <b>raw and
/// global</b> (all customers, including operator-internal events).
/// </remarks>
public interface ITransactionInspectorProvider
{
    /// <summary>Returns the known transactions (running, completed, failed, evicted), newest activity first.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The transaction summaries.</returns>
    Task<IReadOnlyList<TransactionSummary>> GetTransactionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the full detail of one transaction, or <see langword="null"/> when it is unknown.</summary>
    /// <param name="transactionIdHex">The upper-case hex transaction id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The transaction detail, or <see langword="null"/>.</returns>
    Task<TransactionDetail?> GetTransactionAsync(string transactionIdHex, CancellationToken cancellationToken = default);

    /// <summary>Returns the events matching <paramref name="filter"/>, ascending by sequence.</summary>
    /// <param name="filter">The live filter (customer/type/severity/time). Unset fields do not filter.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching events.</returns>
    Task<IReadOnlyList<EventView>> GetEventsAsync(EventLogFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the distinct customer (Partner) ids seen in the event log, for the filter dropdown.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The distinct customer ids, ascending.</returns>
    Task<IReadOnlyList<string>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct event types actually present in the log, for the filter dropdown.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The distinct types, in enum order.</returns>
    /// <remarks>
    /// Derived from the data rather than from <c>Enum.GetValues</c>: offering every type led into an
    /// empty table for the ones no event ever carried (issue #126), the same reason
    /// <see cref="GetCustomerOptionsAsync"/> has always been data-driven.
    /// </remarks>
    Task<IReadOnlyList<EbicsEventType>> GetTypeOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct severities actually present in the log, for the filter dropdown.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The distinct severities, in enum order (ascending by severity).</returns>
    Task<IReadOnlyList<EbicsEventSeverity>> GetSeverityOptionsAsync(CancellationToken cancellationToken = default);
}
