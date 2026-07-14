using EBICO.Core.Crypto;
using EBICO.Core.ReturnCodes;

namespace EBICO.Server.Transactions;

/// <summary>
/// The order-data payload a download response segment carries. The <b>initialisation</b> segment
/// (segment 1) also carries the E002 <see cref="EncryptedTransactionKey"/>, the recipient-key
/// <see cref="EncryptionPubKeyDigest"/> and the <see cref="EncryptionVersion"/> so the client can
/// decrypt the whole transaction; <b>transfer</b> segments carry only <see cref="OrderData"/> (the
/// <c>DataEncryptionInfo</c> is emitted once, in the initialisation response).
/// </summary>
/// <param name="OrderData">The segment's raw (pre-base64) E002-encrypted ciphertext bytes.</param>
/// <param name="EncryptedTransactionKey">The RSA-OAEP-encrypted transaction key (initialisation segment only); otherwise <see langword="null"/>.</param>
/// <param name="EncryptionPubKeyDigest">The SHA-256 digest of the subscriber's encryption key (initialisation segment only); otherwise <see langword="null"/>.</param>
/// <param name="EncryptionVersion">The subscriber encryption key version (initialisation segment only); otherwise <see langword="null"/>.</param>
public sealed record DownloadSegmentPayload(
    byte[] OrderData,
    byte[]? EncryptedTransactionKey = null,
    byte[]? EncryptionPubKeyDigest = null,
    KeyVersion? EncryptionVersion = null);

/// <summary>
/// The outcome of one step of a download transaction (initialisation, transfer or receipt). Everything
/// the pipeline's respond stage needs to build the transaction-shaped <c>ebicsResponse</c>: the return
/// code, the phase to echo, the transaction id, and — for a data-bearing step — the announced segment
/// count (initialisation only), the acknowledged segment number and the segment payload.
/// </summary>
/// <param name="ReturnCode">The return code to report for this step.</param>
/// <param name="Phase">The transaction phase to echo in the response (Initialisation/Transfer/Receipt).</param>
/// <param name="TransactionId">The server-assigned 16-byte transaction id, or <see langword="null"/> when a failure occurred before one was known.</param>
/// <param name="NumSegments">The total number of segments announced in the initialisation response, or <see langword="null"/> (transfer/receipt/error).</param>
/// <param name="SegmentNumber">The delivered segment number in the initialisation/transfer phase, or <see langword="null"/>.</param>
/// <param name="LastSegment">Whether the delivered segment was the last one of the transaction.</param>
/// <param name="Segment">The delivered segment payload, or <see langword="null"/> (receipt/error responses carry no data).</param>
public readonly record struct DownloadTransactionResult(
    EbicsReturnCode ReturnCode,
    EbicsTransactionPhase Phase,
    byte[]? TransactionId = null,
    ulong? NumSegments = null,
    ulong? SegmentNumber = null,
    bool LastSegment = false,
    DownloadSegmentPayload? Segment = null);
