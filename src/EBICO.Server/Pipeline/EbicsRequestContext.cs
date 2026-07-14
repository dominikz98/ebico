using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Versioning;
using EBICO.Server.Transactions;

namespace EBICO.Server.Pipeline;

/// <summary>
/// Carries the data of a single inbound EBICS request through the pipeline stages: the raw XML,
/// the detected version, the parsed request envelope, the extracted order type and — for the signed
/// <c>ebicsRequest</c> — the transaction phase and transaction id used to route the transaction engine.
/// </summary>
public sealed class EbicsRequestContext
{
    /// <summary>Initializes a new <see cref="EbicsRequestContext"/>.</summary>
    /// <param name="requestXml">The raw request XML.</param>
    /// <param name="versionInfo">The detected EBICS version information.</param>
    /// <param name="envelope">The parsed request envelope.</param>
    /// <param name="orderType">The extracted order type, or <see langword="null"/> when absent/not applicable.</param>
    /// <param name="transactionPhase">The transaction phase of a signed <c>ebicsRequest</c>, or <see langword="null"/> when not applicable.</param>
    /// <param name="transactionId">The transaction id carried by a transfer-phase request, or <see langword="null"/> when absent.</param>
    /// <param name="btf">The extracted H005 business transaction format, or <see langword="null"/> when absent/not applicable.</param>
    public EbicsRequestContext(
        string requestXml,
        EbicsVersionInfo versionInfo,
        IEbicsRequestEnvelope envelope,
        string? orderType,
        EbicsTransactionPhase? transactionPhase = null,
        byte[]? transactionId = null,
        BusinessTransactionFormat? btf = null)
    {
        ArgumentNullException.ThrowIfNull(requestXml);
        ArgumentNullException.ThrowIfNull(versionInfo);
        ArgumentNullException.ThrowIfNull(envelope);

        RequestXml = requestXml;
        VersionInfo = versionInfo;
        Envelope = envelope;
        OrderType = orderType;
        TransactionPhase = transactionPhase;
        TransactionId = transactionId;
        Btf = btf;
    }

    /// <summary>The raw request XML as received.</summary>
    public string RequestXml { get; }

    /// <summary>The detected EBICS version information.</summary>
    public EbicsVersionInfo VersionInfo { get; }

    /// <summary>The detected EBICS protocol version.</summary>
    public EbicsVersion Version => VersionInfo.Version;

    /// <summary>The parsed request envelope.</summary>
    public IEbicsRequestEnvelope Envelope { get; }

    /// <summary>The extracted order type (e.g. <c>"HPB"</c>), or <see langword="null"/> when absent.</summary>
    public string? OrderType { get; }

    /// <summary>
    /// The H005 business transaction format extracted from the <c>BTUOrderParams</c>/<c>BTDOrderParams</c>
    /// service element, or <see langword="null"/> for H003/H004 and for H005 requests that carry no BTF.
    /// </summary>
    public BusinessTransactionFormat? Btf { get; }

    /// <summary>
    /// The transaction phase of a signed <c>ebicsRequest</c> (Initialisation/Transfer/Receipt), or
    /// <see langword="null"/> for the unsecured/no-pub-key-digests requests that carry no phase.
    /// </summary>
    public EbicsTransactionPhase? TransactionPhase { get; }

    /// <summary>
    /// The 16-byte transaction id carried in the static header of a transfer-phase request, or
    /// <see langword="null"/> when absent (initialisation phase / non-transaction requests).
    /// </summary>
    public byte[]? TransactionId { get; }
}
