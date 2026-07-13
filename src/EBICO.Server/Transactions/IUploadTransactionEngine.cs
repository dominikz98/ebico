using EBICO.Server.Pipeline;

namespace EBICO.Server.Transactions;

/// <summary>
/// The server-side EBICS upload transaction engine (issue #32): the state machine that owns both
/// phases of an upload. The initialisation phase assigns the transaction id and captures the
/// transaction state; the transfer phase buffers order-data segments and, on the last one, reassembles,
/// decrypts and decompresses the order data.
/// </summary>
/// <remarks>
/// The engine sits beside the single-phase order-handler path: the pipeline routes the generic upload
/// order types (FUL for H003/H004, BTU for H005) in the initialisation phase, and every transfer-phase
/// request (which carries only a transaction id, no order type), to the engine; everything else keeps
/// going through the <see cref="IEbicsOrderHandlerResolver"/>.
/// </remarks>
public interface IUploadTransactionEngine
{
    /// <summary>
    /// Handles the initialisation phase of an upload: validates the subscriber and segment count,
    /// decrypts the transaction key with the bank's private encryption key, assigns a transaction id and
    /// stores the transaction state.
    /// </summary>
    /// <param name="context">The parsed initialisation request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The initialisation outcome, carrying the assigned transaction id on success.</returns>
    Task<UploadTransactionResult> BeginUploadAsync(EbicsRequestContext context, CancellationToken ct = default);

    /// <summary>
    /// Handles the transfer phase of an upload: looks the transaction up by id, buffers the segment and,
    /// on the last segment, reassembles, decrypts and decompresses the order data.
    /// </summary>
    /// <param name="context">The parsed transfer request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The transfer outcome, echoing the transaction id and segment acknowledgement.</returns>
    Task<UploadTransactionResult> ContinueUploadAsync(EbicsRequestContext context, CancellationToken ct = default);
}
