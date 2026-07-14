using EBICO.Server.Pipeline;

namespace EBICO.Server.Transactions;

/// <summary>
/// The server-side EBICS download transaction engine (issue #33): the state machine that owns all three
/// phases of a download. The initialisation phase provisions the order data, compresses, E002-encrypts
/// and segments it, assigns a transaction id and returns the first segment; the transfer phase serves
/// the remaining segments; the receipt phase processes the client's acknowledgement.
/// </summary>
/// <remarks>
/// The engine sits beside the upload engine and the single-phase order-handler path. The pipeline routes
/// the generic download order types (FDL for H003/H004, BTD for H005) in the initialisation phase to
/// this engine, the receipt phase (download-only) to this engine, and a transfer-phase request to this
/// engine when its transaction id belongs to a download transaction (<see cref="OwnsTransaction"/>);
/// otherwise the transfer goes to the upload engine.
/// </remarks>
public interface IDownloadTransactionEngine
{
    /// <summary>
    /// Handles the initialisation phase of a download: validates the subscriber, provisions the order
    /// data, compresses + E002-encrypts + segments it, assigns a transaction id, stores the transaction
    /// and returns the segment count and the first segment.
    /// </summary>
    /// <param name="context">The parsed initialisation request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The initialisation outcome, carrying the transaction id, segment count and first segment on success.</returns>
    Task<DownloadTransactionResult> BeginDownloadAsync(EbicsRequestContext context, CancellationToken ct = default);

    /// <summary>
    /// Handles the transfer phase of a download: looks the transaction up by id and returns the
    /// requested segment.
    /// </summary>
    /// <param name="context">The parsed transfer request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The transfer outcome, echoing the transaction id and carrying the requested segment.</returns>
    Task<DownloadTransactionResult> ContinueDownloadAsync(EbicsRequestContext context, CancellationToken ct = default);

    /// <summary>
    /// Handles the receipt phase of a download: processes the client's positive/negative acknowledgement,
    /// removes the transaction and — on a negative receipt — re-enqueues the order data.
    /// </summary>
    /// <param name="context">The parsed receipt request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The receipt outcome (post-processing done/skipped, or unknown transaction id).</returns>
    Task<DownloadTransactionResult> AcknowledgeReceiptAsync(EbicsRequestContext context, CancellationToken ct = default);

    /// <summary>
    /// Whether <paramref name="transactionId"/> belongs to a download transaction held by this engine.
    /// Used by the pipeline to route a transfer-phase request (which carries only a transaction id) to
    /// the download or the upload engine.
    /// </summary>
    /// <param name="transactionId">The transaction id from the request static header, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a download transaction with this id exists; otherwise <see langword="false"/>.</returns>
    bool OwnsTransaction(byte[]? transactionId);
}
