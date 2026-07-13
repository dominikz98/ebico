using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.ReturnCodes;

namespace EBICO.Server.Pipeline;

/// <summary>
/// The encrypted key-management payload a download order (currently only <c>HPB</c>) contributes to
/// its response: the E002-hybrid-encrypted order data plus the digest of the recipient's encryption
/// key. The pipeline hands these to
/// <see cref="EBICO.Server.ReturnCodes.EbicsResponseFactory.BuildKeyManagementResponse(EBICO.Core.EbicsVersion, EbicsKeyManagementPayload)"/>,
/// which fills the <c>DataTransfer</c> of the <c>ebicsKeyManagementResponse</c>.
/// </summary>
/// <param name="EncryptedTransactionKey">The RSA-OAEP-encrypted transaction key (<c>DataEncryptionInfo/TransactionKey</c>).</param>
/// <param name="EncryptedOrderData">The AES-encrypted, compressed order data (<c>DataTransfer/OrderData</c>).</param>
/// <param name="EncryptionPubKeyDigest">The SHA-256 digest of the recipient's (subscriber's) encryption public key.</param>
/// <param name="EncryptionVersion">The recipient's encryption key version (e.g. <c>E002</c>).</param>
public sealed record EbicsKeyManagementPayload(
    byte[] EncryptedTransactionKey,
    byte[] EncryptedOrderData,
    byte[] EncryptionPubKeyDigest,
    KeyVersion EncryptionVersion);

/// <summary>
/// The outcome of an <see cref="IEbicsOrderHandler"/>.
/// </summary>
/// <param name="ReturnCode">The return code to report for the handled order.</param>
/// <param name="Payload">
/// The encrypted key-management payload for a successful download order (HPB), or
/// <see langword="null"/> for the pure return-code responses of INI/HIA and every error path.
/// </param>
public readonly record struct EbicsOrderResult(EbicsReturnCode ReturnCode, EbicsKeyManagementPayload? Payload = null);

/// <summary>
/// Extension point for the pipeline's <em>handle</em> stage: processes one EBICS order type of one
/// protocol version. The skeleton (#25) registers no handlers, so every recognized request is
/// answered with <see cref="EbicsReturnCode.UnsupportedOrderType"/>. Concrete handlers (INI/HIA/HPB,
/// then the transaction engine) are added by the M3/M4 issues.
/// </summary>
public interface IEbicsOrderHandler
{
    /// <summary>The protocol version this handler serves.</summary>
    EbicsVersion Version { get; }

    /// <summary>The order type this handler serves (e.g. <c>"HPB"</c>).</summary>
    string OrderType { get; }

    /// <summary>Handles the order described by <paramref name="context"/>.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The order result.</returns>
    Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default);
}
