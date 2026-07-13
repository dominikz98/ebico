using EBICO.Core.ReturnCodes;

namespace EBICO.Server.Transactions;

/// <summary>
/// The outcome of one step of an upload transaction (initialisation or transfer). Everything the
/// pipeline's respond stage needs to build the transaction-shaped <c>ebicsResponse</c>: the return
/// code, the phase to echo, and — once assigned — the transaction id and (transfer phase) the segment
/// acknowledgement.
/// </summary>
/// <param name="ReturnCode">The return code to report for this step.</param>
/// <param name="Phase">The transaction phase to echo in the response (<c>Initialisation</c>/<c>Transfer</c>).</param>
/// <param name="TransactionId">The server-assigned 16-byte transaction id, or <see langword="null"/> when a failure occurred before one was assigned.</param>
/// <param name="SegmentNumber">The acknowledged segment number in the transfer phase, or <see langword="null"/>.</param>
/// <param name="LastSegment">Whether the acknowledged segment was the last one of the transaction.</param>
public readonly record struct UploadTransactionResult(
    EbicsReturnCode ReturnCode,
    EbicsTransactionPhase Phase,
    byte[]? TransactionId = null,
    ulong? SegmentNumber = null,
    bool LastSegment = false);
