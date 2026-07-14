using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;

namespace EBICO.Server.Orders;

/// <summary>
/// The decoded, order-type-specific input handed to an <see cref="IUploadOrderProcessor"/> once an
/// upload transaction has been fully received, decrypted and decompressed (issue #39).
/// </summary>
/// <param name="Subscriber">The subscriber that submitted the upload.</param>
/// <param name="Version">The protocol version the upload ran under.</param>
/// <param name="EffectiveOrderType">The resolved classical order-type code (e.g. <c>"CCT"</c>), not the generic <c>FUL</c>/<c>BTU</c>.</param>
/// <param name="OrderData">The plaintext order data (the decoded pain payload).</param>
/// <param name="TransactionIdHex">The hex transaction id, for correlating lifecycle events; may be <see langword="null"/>.</param>
public readonly record struct UploadOrderContext(
    SubscriberKeyRef Subscriber,
    EbicsVersion Version,
    string EffectiveOrderType,
    byte[] OrderData,
    string? TransactionIdHex);

/// <summary>
/// The outcome of processing an uploaded order. Carries the EBICS return code the transfer response
/// should report: <see cref="EbicsReturnCode.Ok"/> when the order was accepted (and any follow-up data
/// filed) or <see cref="EbicsReturnCode.InvalidOrderDataFormat"/> when its payload failed validation.
/// </summary>
/// <param name="ReturnCode">The return code to surface for the completing transfer step.</param>
public readonly record struct UploadOrderResult(EbicsReturnCode ReturnCode)
{
    /// <summary>A successful processing result (<see cref="EbicsReturnCode.Ok"/>).</summary>
    public static UploadOrderResult Accepted { get; } = new(EbicsReturnCode.Ok);

    /// <summary>A rejected processing result (<see cref="EbicsReturnCode.InvalidOrderDataFormat"/>).</summary>
    public static UploadOrderResult Rejected { get; } = new(EbicsReturnCode.InvalidOrderDataFormat);
}

/// <summary>
/// Processes the business payload of a completed upload transaction — the extension point the upload
/// engine calls after reassembling/decrypting/decompressing the order data (issue #39). An
/// implementation validates the payload for its order type and files any follow-up data (e.g. a status
/// report for later download). Order types the processor does not handle are left to the engine's
/// default behaviour (retain the plaintext on the transaction, no further processing).
/// </summary>
/// <remarks>
/// The default registration is the <see cref="SepaPaymentUploadProcessor"/> (SEPA payments); a caller can
/// substitute or extend it via <c>TryAddSingleton</c> before <c>AddEbicoServer</c>. The processor runs
/// after the strict per-order authorisation check (issue #38) has already passed in the initialisation.
/// </remarks>
public interface IUploadOrderProcessor
{
    /// <summary>Whether this processor handles the given resolved order type.</summary>
    /// <param name="effectiveOrderType">The resolved classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <see cref="ProcessAsync"/> should be invoked for this order type.</returns>
    bool CanProcess(string? effectiveOrderType);

    /// <summary>
    /// Processes a completed upload order: validates the payload and files any follow-up data. Only
    /// called when <see cref="CanProcess"/> returned <see langword="true"/> for the order type.
    /// </summary>
    /// <param name="context">The decoded order context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The processing outcome (the return code the transfer response should report).</returns>
    Task<UploadOrderResult> ProcessAsync(UploadOrderContext context, CancellationToken ct = default);
}
