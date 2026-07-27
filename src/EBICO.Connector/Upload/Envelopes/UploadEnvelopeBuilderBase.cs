using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Upload.Envelopes;

/// <summary>
/// Shared base for the version-specific upload envelope builders. It centralises the constants and the
/// return-code projection that are identical across versions; the envelope construction and response
/// deserialization stay version-specific (the generated bindings live in per-version namespaces).
/// </summary>
internal abstract class UploadEnvelopeBuilderBase : IUploadEnvelopeBuilder
{
    /// <summary>
    /// The <c>SecurityMedium</c> value for transaction orders. <b>⚠️ Spec-Vorbehalt:</b> <c>"0000"</c>
    /// ("no medium") mirrors the value the server tests and onboarding builders use; not verified
    /// against the official Annex.
    /// </summary>
    protected const string SecurityMedium = "0000";

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public abstract IAuthSignedRequestEnvelope BuildInitRequest(in UploadInitContext ctx);

    /// <inheritdoc />
    public abstract IAuthSignedRequestEnvelope BuildTransferRequest(in UploadTransferContext ctx);

    /// <inheritdoc />
    public abstract UploadResponseView ParseInitResponse(string responseXml);

    /// <inheritdoc />
    public abstract UploadResponseView ParseTransferResponse(string responseXml);

    /// <summary>
    /// Combines the two return-code slots of an <c>ebicsResponse</c> — and the header's report text —
    /// into the effective outcome. Technical codes live in <c>header/mutable/ReturnCode</c>, business
    /// codes in <c>body/ReturnCode</c>, and the unused slot carries EBICS_OK; the non-OK slot wins.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="EbicsReturnCodes.CombineOutcome(string?, string?, string?)"/> so the
    /// report text is taken from the same slot as the winning code. Reading the text unconditionally
    /// from the header made business faults report <c>EBICS_OK</c> alongside their error code (#124).
    /// </remarks>
    /// <param name="headerCode">The mutable-header (technical) return code.</param>
    /// <param name="headerText">The mutable-header report text.</param>
    /// <param name="bodyCode">The body (business) return code.</param>
    /// <returns>The effective return code paired with a matching report text.</returns>
    protected static EbicsResponseOutcome CombineOutcome(string? headerCode, string? headerText, string? bodyCode)
        => EbicsReturnCodes.CombineOutcome(headerCode, headerText, bodyCode);
}
