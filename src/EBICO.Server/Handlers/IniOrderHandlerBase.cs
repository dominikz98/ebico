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
/// Base for the version-specific INI order handlers. INI (<c>OrderType == "INI"</c>) receives the
/// subscriber's bank-technical signature key (<c>A00x</c>) inside an <c>ebicsUnsecuredRequest</c>,
/// stores it and moves the subscriber from <see cref="SubscriberState.New"/> to
/// <see cref="SubscriberState.Initialized"/>.
/// </summary>
/// <remarks>
/// <para>
/// The version-agnostic flow — resolving the subscriber, enforcing the "must exist and be
/// <see cref="SubscriberState.New"/>" precondition, key-version policy, storing the key and the
/// lifecycle transition — lives here. The version-specific step (casting the envelope and reading the
/// <c>A00x</c> key out of the <c>S001</c> <c>RSAKeyValue</c> resp. the <c>S002</c> <c>X509Data</c>)
/// is <see cref="ExtractIniOrderData"/>.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> re-sending INI once the subscriber is no longer <c>New</c> is rejected
/// with <see cref="EbicsReturnCode.InvalidUserOrUserState"/>; the response is unsigned (response
/// AuthSignature/X002 is M4). See <c>docs/server/ini.md</c>.
/// </para>
/// </remarks>
public abstract class IniOrderHandlerBase : IEbicsOrderHandler
{
    /// <summary>The order type served by INI handlers.</summary>
    public const string IniOrderType = "INI";

    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager used to resolve and transition the subscriber.</param>
    /// <param name="keyStore">The server key store the signature key is written to.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected IniOrderHandlerBase(IMasterDataManager masterData, IServerKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(keyStore);

        _masterData = masterData;
        _keyStore = keyStore;
    }

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public string OrderType => IniOrderType;

    /// <inheritdoc />
    public async Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Decode the order data; any decode/validation fault surfaces as EbicsOrderDataException and is
        // mapped to InvalidOrderDataFormat by the pipeline's central error mapper.
        var data = OrderDataFault.Wrap(() =>
        {
            var extracted = ExtractIniOrderData(context);

            // Signature key only: reject an encryption/authentication version smuggled into the
            // SignatureVersion element, and reject a version not permitted for this protocol version
            // (e.g. A006 on H004).
            if (extracted.Version.Purpose != KeyPurpose.Signature)
            {
                throw new EbicsOrderDataException("The INI order carries a non-signature key version.");
            }

            _ = KeyVersions.EnsurePermitted(extracted.Version, Version);
            return extracted;
        });

        if (!HostId.TryCreate(data.HostId, out var hostId)
            || !PartnerId.TryCreate(data.PartnerId, out var partnerId)
            || !UserId.TryCreate(data.UserId, out var userId))
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.New)
        {
            // Unknown subscriber, or already initialized (re-INI is rejected).
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Store the bank-technical signature key, then advance New -> Initialized. The transition is
        // known to be valid here (state == New), so it cannot fail on the lifecycle rules.
        var keyRef = new SubscriberKeyRef(hostId, partnerId, userId);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(data.Key.ToPublicOnly(), data.Version), ct).ConfigureAwait(false);
        _ = await _masterData.TransitionSubscriberAsync(hostId, partnerId, userId, SubscriberState.Initialized, ct).ConfigureAwait(false);

        return new EbicsOrderResult(EbicsReturnCode.Ok);
    }

    /// <summary>
    /// Reads the subscriber identifiers and the <c>A00x</c> signature key out of the version-specific
    /// <c>ebicsUnsecuredRequest</c> in <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The request context (its <see cref="EbicsRequestContext.Envelope"/> is the unsecured request).</param>
    /// <returns>The extracted identifiers, key material and key version.</returns>
    /// <remarks>Implementations may throw the low-level order-data failures wrapped by <see cref="OrderDataFault"/> into <see cref="EbicsOrderDataException"/> (mapped to <see cref="EbicsReturnCode.InvalidOrderDataFormat"/>).</remarks>
    protected abstract IniKeyData ExtractIniOrderData(EbicsRequestContext context);

    /// <summary>Decompresses and deserializes the embedded order-data document to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The order-data binding type (<c>S001</c>/<c>S002</c> <c>SignaturePubKeyOrderDataType</c>).</typeparam>
    /// <param name="compressedOrderData">The <c>OrderData</c> value (already base64-decoded by the binding), or <see langword="null"/>.</param>
    /// <returns>The deserialized order-data graph.</returns>
    /// <exception cref="InvalidDataException"><paramref name="compressedOrderData"/> is missing or not in the expected compressed format.</exception>
    protected static T DeserializeOrderData<T>(byte[]? compressedOrderData)
    {
        var compressed = compressedOrderData
            ?? throw new InvalidDataException("The INI request carries no order data.");
        var xml = Encoding.UTF8.GetString(EbicsCompression.Decompress(compressed));
        return EbicsXmlSerializer.Deserialize<T>(xml);
    }

    /// <summary>
    /// The subscriber identifiers and signature key extracted from an INI request. Identifiers are the
    /// raw header strings (validated later); <see cref="Key"/> may carry a private key and is reduced
    /// to public-only before storage.
    /// </summary>
    /// <param name="HostId">The raw <c>HostID</c> from the request header.</param>
    /// <param name="PartnerId">The raw <c>PartnerID</c> from the request header.</param>
    /// <param name="UserId">The raw <c>UserID</c> from the request header.</param>
    /// <param name="Key">The reconstructed RSA public key.</param>
    /// <param name="Version">The signature key version (e.g. <c>A005</c>).</param>
    protected sealed record IniKeyData(string? HostId, string? PartnerId, string? UserId, RsaKeyMaterial Key, KeyVersion Version);
}
