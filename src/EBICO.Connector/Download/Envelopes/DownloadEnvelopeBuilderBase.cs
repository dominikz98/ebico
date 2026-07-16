using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Download.Envelopes;

/// <summary>
/// Shared base for the version-specific download envelope builders. It centralises the constants and
/// the return-code projection that are identical across versions; the envelope construction and
/// response deserialization stay version-specific (the generated bindings live in per-version
/// namespaces, and each version carries its own nested <c>DateRange</c> element type).
/// </summary>
internal abstract class DownloadEnvelopeBuilderBase : IDownloadEnvelopeBuilder
{
    /// <summary>
    /// The <c>SecurityMedium</c> value for transaction orders. <b>⚠️ Spec-Vorbehalt:</b> <c>"0000"</c>
    /// ("no medium") mirrors the value the server tests, onboarding and upload builders use; not
    /// verified against the official Annex.
    /// </summary>
    protected const string SecurityMedium = "0000";

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public abstract IAuthSignedRequestEnvelope BuildInitRequest(in DownloadInitContext ctx);

    /// <inheritdoc />
    public abstract IAuthSignedRequestEnvelope BuildTransferRequest(in DownloadTransferContext ctx);

    /// <inheritdoc />
    public abstract IAuthSignedRequestEnvelope BuildReceiptRequest(in DownloadReceiptContext ctx);

    /// <inheritdoc />
    public abstract DownloadInitResponseView ParseInitResponse(string responseXml);

    /// <inheritdoc />
    public abstract DownloadTransferResponseView ParseTransferResponse(string responseXml);

    /// <inheritdoc />
    public abstract DownloadReceiptResponseView ParseReceiptResponse(string responseXml);

    /// <summary>
    /// Combines the two return-code slots of an <c>ebicsResponse</c> into the effective outcome:
    /// technical codes live in <c>header/mutable/ReturnCode</c>, business codes in <c>body/ReturnCode</c>,
    /// and the unused slot carries EBICS_OK. The non-OK slot (if any) wins; otherwise EBICS_OK.
    /// </summary>
    /// <param name="headerCode">The mutable-header (technical) return code.</param>
    /// <param name="bodyCode">The body (business) return code.</param>
    /// <returns>The effective return code.</returns>
    protected static string CombineReturnCode(string? headerCode, string? bodyCode)
    {
        if (!string.IsNullOrEmpty(headerCode) && headerCode != EbicsReturnCode.OkCode)
        {
            return headerCode;
        }

        if (!string.IsNullOrEmpty(bodyCode) && bodyCode != EbicsReturnCode.OkCode)
        {
            return bodyCode;
        }

        return EbicsReturnCode.OkCode;
    }
}
