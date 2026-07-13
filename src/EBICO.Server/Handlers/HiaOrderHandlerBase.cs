using System.Text;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.State;

namespace EBICO.Server.Handlers;

/// <summary>
/// Base for the version-specific HIA order handlers. HIA (<c>OrderType == "HIA"</c>) receives the
/// subscriber's <b>authentication</b> key (<c>X00x</c>) and <b>encryption</b> key (<c>E00x</c>) inside
/// an <c>ebicsUnsecuredRequest</c>, stores both and moves the subscriber from
/// <see cref="SubscriberState.Initialized"/> to <see cref="SubscriberState.Ready"/>.
/// </summary>
/// <remarks>
/// <para>
/// The version-agnostic flow — enforcing the "must exist and be
/// <see cref="SubscriberState.Initialized"/>" precondition, the key-version policy for both keys,
/// storing them and the lifecycle transition — lives here. The version-specific step (casting the
/// envelope and reading the two keys out of the <c>PubKeyValue</c> <c>RSAKeyValue</c> resp. the
/// <c>X509Data</c> certificate) is <see cref="ExtractHiaOrderData"/>.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> HIA is only accepted once INI has run (state
/// <see cref="SubscriberState.Initialized"/>); the INI-before-HIA ordering is thereby enforced and a
/// re-sent HIA is rejected with <see cref="EbicsReturnCode.InvalidUserOrUserState"/>. The
/// <see cref="SubscriberState.Ready"/> state is reached without a separate activation step, and the
/// response is unsigned (response AuthSignature/X002 is M4). See <c>docs/server/hia.md</c>.
/// </para>
/// </remarks>
public abstract class HiaOrderHandlerBase : IEbicsOrderHandler
{
    /// <summary>The order type served by HIA handlers.</summary>
    public const string HiaOrderType = "HIA";

    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager used to resolve and transition the subscriber.</param>
    /// <param name="keyStore">The server key store the authentication and encryption keys are written to.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected HiaOrderHandlerBase(IMasterDataManager masterData, IServerKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(keyStore);

        _masterData = masterData;
        _keyStore = keyStore;
    }

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public string OrderType => HiaOrderType;

    /// <inheritdoc />
    public async Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Decode the order data; any decode/validation fault surfaces as EbicsOrderDataException and is
        // mapped to InvalidOrderDataFormat by the pipeline's central error mapper.
        var data = OrderDataFault.Wrap(() =>
        {
            var extracted = ExtractHiaOrderData(context);

            // The authentication key must carry an X00x version and the encryption key an E00x version;
            // reject a purpose-mismatched version smuggled into the wrong element, and reject a version
            // not permitted for this protocol version (e.g. E001 on H005).
            if (extracted.AuthVersion.Purpose != KeyPurpose.Authentication
                || extracted.EncVersion.Purpose != KeyPurpose.Encryption)
            {
                throw new EbicsOrderDataException("The HIA order carries a purpose-mismatched key version.");
            }

            _ = KeyVersions.EnsurePermitted(extracted.AuthVersion, Version);
            _ = KeyVersions.EnsurePermitted(extracted.EncVersion, Version);
            return extracted;
        });

        if (!HostId.TryCreate(data.HostId, out var hostId)
            || !PartnerId.TryCreate(data.PartnerId, out var partnerId)
            || !UserId.TryCreate(data.UserId, out var userId))
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.Initialized)
        {
            // Unknown subscriber, INI not yet run (still New), or already past HIA (re-HIA is rejected).
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Store the authentication and encryption keys (purpose is derived from each key version), then
        // advance Initialized -> Ready. The transition is known to be valid here (state == Initialized).
        var keyRef = new SubscriberKeyRef(hostId, partnerId, userId);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(data.AuthKey.ToPublicOnly(), data.AuthVersion), ct).ConfigureAwait(false);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(data.EncKey.ToPublicOnly(), data.EncVersion), ct).ConfigureAwait(false);
        _ = await _masterData.TransitionSubscriberAsync(hostId, partnerId, userId, SubscriberState.Ready, ct).ConfigureAwait(false);

        return new EbicsOrderResult(EbicsReturnCode.Ok);
    }

    /// <summary>
    /// Reads the subscriber identifiers and the <c>X00x</c> authentication and <c>E00x</c> encryption
    /// keys out of the version-specific <c>ebicsUnsecuredRequest</c> in <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The request context (its <see cref="EbicsRequestContext.Envelope"/> is the unsecured request).</param>
    /// <returns>The extracted identifiers, key material and key versions.</returns>
    /// <remarks>Implementations may throw the low-level order-data failures wrapped by <see cref="OrderDataFault"/> into <see cref="EbicsOrderDataException"/> (mapped to <see cref="EbicsReturnCode.InvalidOrderDataFormat"/>).</remarks>
    protected abstract HiaKeyData ExtractHiaOrderData(EbicsRequestContext context);

    /// <summary>Decompresses and deserializes the embedded order-data document to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The order-data binding type (<c>H00x</c> <c>HiaRequestOrderDataType</c>).</typeparam>
    /// <param name="compressedOrderData">The <c>OrderData</c> value (already base64-decoded by the binding), or <see langword="null"/>.</param>
    /// <returns>The deserialized order-data graph.</returns>
    /// <exception cref="InvalidDataException"><paramref name="compressedOrderData"/> is missing or not in the expected compressed format.</exception>
    protected static T DeserializeOrderData<T>(byte[]? compressedOrderData)
    {
        var compressed = compressedOrderData
            ?? throw new InvalidDataException("The HIA request carries no order data.");
        var xml = Encoding.UTF8.GetString(EbicsCompression.Decompress(compressed));
        return EbicsXmlSerializer.Deserialize<T>(xml);
    }

    /// <summary>
    /// The subscriber identifiers and the authentication/encryption keys extracted from a HIA request.
    /// Identifiers are the raw header strings (validated later); the keys may carry a private key and are
    /// reduced to public-only before storage.
    /// </summary>
    /// <param name="HostId">The raw <c>HostID</c> from the request header.</param>
    /// <param name="PartnerId">The raw <c>PartnerID</c> from the request header.</param>
    /// <param name="UserId">The raw <c>UserID</c> from the request header.</param>
    /// <param name="AuthKey">The reconstructed RSA authentication public key.</param>
    /// <param name="AuthVersion">The authentication key version (e.g. <c>X002</c>).</param>
    /// <param name="EncKey">The reconstructed RSA encryption public key.</param>
    /// <param name="EncVersion">The encryption key version (e.g. <c>E002</c>).</param>
    protected sealed record HiaKeyData(
        string? HostId,
        string? PartnerId,
        string? UserId,
        RsaKeyMaterial AuthKey,
        KeyVersion AuthVersion,
        RsaKeyMaterial EncKey,
        KeyVersion EncVersion);
}
