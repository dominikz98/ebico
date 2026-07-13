using System.Security.Cryptography;
using System.Text;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;

namespace EBICO.Server.Handlers;

/// <summary>
/// Base for the version-specific HSA order handlers. HSA (<c>OrderType == "HSA"</c>) is the legacy
/// combined key-initialisation order that carries the subscriber's <b>authentication</b> key
/// (<c>X00x</c>) and <b>encryption</b> key (<c>E00x</c>) inside an <c>ebicsUnsecuredRequest</c> — the
/// same envelope form as HIA. It stores both keys and moves the subscriber from
/// <see cref="SubscriberState.Initialized"/> to <see cref="SubscriberState.Ready"/>.
/// </summary>
/// <remarks>
/// <para>
/// HSA exists only for <b>H003/H004</b> (it was removed in H005, where HIA is used exclusively), so
/// there is no H005 handler. Functionally HSA mirrors HIA (see <see cref="HiaOrderHandlerBase"/>); the
/// only differences are the order type and the order-data root element (<c>HSARequestOrderData</c>).
/// The version-agnostic flow — enforcing the "must exist and be
/// <see cref="SubscriberState.Initialized"/>" precondition, the key-version policy for both keys,
/// storing them and the lifecycle transition — lives here. The version-specific step (casting the
/// envelope and reading the two keys out of the <c>PubKeyValue</c> <c>RSAKeyValue</c>) is
/// <see cref="ExtractHsaOrderData"/>.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> HSA is, per the schema, signed with the subscriber's FTAM signature key;
/// that order signature is <b>not</b> verified (consistent with INI/HIA/HPB — request/response
/// signatures are M4). HSA is only accepted once INI has run (state
/// <see cref="SubscriberState.Initialized"/>); a re-sent HSA is rejected with
/// <see cref="EbicsReturnCode.InvalidUserOrUserState"/>. See <c>docs/server/hca-hcs-spr-hsa.md</c>.
/// </para>
/// </remarks>
public abstract class HsaOrderHandlerBase : IEbicsOrderHandler
{
    /// <summary>The order type served by HSA handlers.</summary>
    public const string HsaOrderType = "HSA";

    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager used to resolve and transition the subscriber.</param>
    /// <param name="keyStore">The server key store the authentication and encryption keys are written to.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected HsaOrderHandlerBase(IMasterDataManager masterData, IServerKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(keyStore);

        _masterData = masterData;
        _keyStore = keyStore;
    }

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public string OrderType => HsaOrderType;

    /// <inheritdoc />
    public async Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        HsaKeyData data;
        try
        {
            data = ExtractHsaOrderData(context);

            // The authentication key must carry an X00x version and the encryption key an E00x version;
            // reject a purpose-mismatched version smuggled into the wrong element, and reject a version
            // not permitted for this protocol version.
            if (data.AuthVersion.Purpose != KeyPurpose.Authentication
                || data.EncVersion.Purpose != KeyPurpose.Encryption)
            {
                return new EbicsOrderResult(EbicsReturnCode.InvalidOrderDataFormat);
            }

            _ = KeyVersions.EnsurePermitted(data.AuthVersion, Version);
            _ = KeyVersions.EnsurePermitted(data.EncVersion, Version);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or KeyMaterialException
            or KeyVersionNotPermittedException or InvalidKeyVersionException or CryptographicException
            or ArgumentException or InvalidOperationException)
        {
            // The order data could not be decompressed/deserialized, the key material could not be
            // reconstructed, or one of the key versions is unusable/unpermitted.
            return new EbicsOrderResult(EbicsReturnCode.InvalidOrderDataFormat);
        }

        if (!HostId.TryCreate(data.HostId, out var hostId)
            || !PartnerId.TryCreate(data.PartnerId, out var partnerId)
            || !UserId.TryCreate(data.UserId, out var userId))
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.Initialized)
        {
            // Unknown subscriber, INI not yet run (still New), or already past HSA/HIA (re-send rejected).
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
    /// <remarks>Implementations may throw any of the exceptions caught by <see cref="HandleAsync"/>; those map to <see cref="EbicsReturnCode.InvalidOrderDataFormat"/>.</remarks>
    protected abstract HsaKeyData ExtractHsaOrderData(EbicsRequestContext context);

    /// <summary>Decompresses and deserializes the embedded order-data document to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The order-data binding type (<c>H00x</c> <c>HsaRequestOrderDataType</c>).</typeparam>
    /// <param name="compressedOrderData">The <c>OrderData</c> value (already base64-decoded by the binding), or <see langword="null"/>.</param>
    /// <returns>The deserialized order-data graph.</returns>
    /// <exception cref="InvalidDataException"><paramref name="compressedOrderData"/> is missing or not in the expected compressed format.</exception>
    protected static T DeserializeOrderData<T>(byte[]? compressedOrderData)
    {
        var compressed = compressedOrderData
            ?? throw new InvalidDataException("The HSA request carries no order data.");
        var xml = Encoding.UTF8.GetString(EbicsCompression.Decompress(compressed));
        return EbicsXmlSerializer.Deserialize<T>(xml);
    }

    /// <summary>
    /// The subscriber identifiers and the authentication/encryption keys extracted from an HSA request.
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
    protected sealed record HsaKeyData(
        string? HostId,
        string? PartnerId,
        string? UserId,
        RsaKeyMaterial AuthKey,
        KeyVersion AuthVersion,
        RsaKeyMaterial EncKey,
        KeyVersion EncVersion);
}
