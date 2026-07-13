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
/// Base for the version-specific HCA order handlers. HCA (<c>OrderType == "HCA"</c>) is the key-change
/// order that replaces an already-onboarded subscriber's <b>authentication</b> key (<c>X00x</c>) and
/// <b>encryption</b> key (<c>E00x</c>). Unlike INI/HIA it arrives as a signed <c>ebicsRequest</c> whose
/// order data is E002-encrypted for the <b>bank's</b> public encryption key; the server decrypts it
/// with its own private encryption key (the reverse of HPB), then stores the new keys. The subscriber
/// stays <see cref="SubscriberState.Ready"/> — a key change is not a lifecycle transition.
/// </summary>
/// <remarks>
/// <para>
/// The version-agnostic flow lives here: read the identifiers and the encrypted order data
/// (<see cref="ExtractEnvelope"/>), fetch the bank key pair from <see cref="IServerBankKeyStore"/>,
/// E002-decrypt + decompress the order data, extract the two new keys (<see cref="ParseOrderData"/>),
/// enforce the key-version policy, require the subscriber to exist and be
/// <see cref="SubscriberState.Ready"/>, then upsert both keys (the store replaces the existing key per
/// purpose). The version-specific steps are the envelope cast and the key extraction
/// (H003/H004 <c>RSAKeyValue</c>, H005 <c>X509Data</c>).
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the order data is only <em>decrypted</em>, not authenticated — the order
/// signature (ES, signed with the current signature key) and the X002 authentication signature are
/// <b>not</b> verified (M4, consistent with INI/HIA/HPB). This is a simplified single-phase handling of
/// the signed upload (the generic upload transaction with segmentation is M4). See
/// <c>docs/server/hca-hcs-spr-hsa.md</c>.
/// </para>
/// </remarks>
public abstract class HcaOrderHandlerBase : IEbicsOrderHandler
{
    /// <summary>The order type served by HCA handlers.</summary>
    public const string HcaOrderType = "HCA";

    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;
    private readonly IServerBankKeyStore _bankKeyStore;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager used to resolve the subscriber.</param>
    /// <param name="keyStore">The server key store the new authentication and encryption keys are written to.</param>
    /// <param name="bankKeyStore">The store providing the bank's own key pair (its private encryption key decrypts the order data).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected HcaOrderHandlerBase(IMasterDataManager masterData, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
    {
        ArgumentNullException.ThrowIfNull(masterData);
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(bankKeyStore);

        _masterData = masterData;
        _keyStore = keyStore;
        _bankKeyStore = bankKeyStore;
    }

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public virtual string OrderType => HcaOrderType;

    /// <inheritdoc />
    public async Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A non-conforming envelope surfaces as EbicsOrderDataException -> InvalidOrderDataFormat.
        var envelope = OrderDataFault.Wrap(() => ExtractEnvelope(context));

        if (!HostId.TryCreate(envelope.HostId, out var hostId)
            || !PartnerId.TryCreate(envelope.PartnerId, out var partnerId)
            || !UserId.TryCreate(envelope.UserId, out var userId))
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // The order data is encrypted for the bank's E002 key; decrypt it with the bank's private key
        // (GetOrCreateAsync returns a pair that carries the private part).
        var bankKeys = await _bankKeyStore.GetOrCreateAsync(hostId, ct).ConfigureAwait(false);

        // Decrypt and parse the order data; undecryptable/undecompressable/undeserializable data,
        // unreconstructable key material or an unusable/unpermitted key version all surface as
        // EbicsOrderDataException -> InvalidOrderDataFormat.
        var keys = OrderDataFault.Wrap(() =>
        {
            var parsed = DecryptAndParse(envelope, bankKeys);

            if (parsed.AuthVersion.Purpose != KeyPurpose.Authentication
                || parsed.EncVersion.Purpose != KeyPurpose.Encryption)
            {
                throw new EbicsOrderDataException("The HCA order carries a purpose-mismatched key version.");
            }

            _ = KeyVersions.EnsurePermitted(parsed.AuthVersion, Version);
            _ = KeyVersions.EnsurePermitted(parsed.EncVersion, Version);
            return parsed;
        });

        // HCA requires a fully onboarded subscriber (INI + HIA done). The key change keeps state Ready.
        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.Ready)
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Replace the authentication and encryption keys (the store upserts per purpose).
        var keyRef = new SubscriberKeyRef(hostId, partnerId, userId);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(keys.AuthKey.ToPublicOnly(), keys.AuthVersion), ct).ConfigureAwait(false);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(keys.EncKey.ToPublicOnly(), keys.EncVersion), ct).ConfigureAwait(false);

        return new EbicsOrderResult(EbicsReturnCode.Ok);
    }

    /// <summary>
    /// Reads the subscriber identifiers and the encrypted order-data ciphertexts (the RSA-OAEP-encrypted
    /// transaction key and the AES-encrypted order data) out of the version-specific <c>ebicsRequest</c>.
    /// </summary>
    /// <param name="context">The request context (its <see cref="EbicsRequestContext.Envelope"/> is the signed request).</param>
    /// <returns>The extracted identifiers and ciphertexts.</returns>
    /// <exception cref="InvalidDataException">The envelope is not the expected signed request.</exception>
    protected abstract HcaEnvelope ExtractEnvelope(EbicsRequestContext context);

    /// <summary>
    /// Deserializes the decrypted, decompressed order-data XML and extracts the new authentication and
    /// encryption keys (version-specific: H003/H004 read <c>RSAKeyValue</c>, H005 reads <c>X509Data</c>).
    /// </summary>
    /// <param name="orderDataXml">The decrypted and decompressed order-data XML.</param>
    /// <returns>The extracted keys and their versions.</returns>
    /// <remarks>Implementations may throw the low-level order-data failures wrapped by <see cref="OrderDataFault"/> into <see cref="EbicsOrderDataException"/> (mapped to <see cref="EbicsReturnCode.InvalidOrderDataFormat"/>).</remarks>
    protected abstract HcaKeys ParseOrderData(string orderDataXml);

    // Decrypts the order data with the bank's private E002 key, decompresses it and delegates the
    // version-specific key extraction. Missing ciphertexts surface as InvalidDataException (-> 090004).
    private HcaKeys DecryptAndParse(HcaEnvelope envelope, BankKeyPair bankKeys)
    {
        var transactionKey = envelope.EncryptedTransactionKey
            ?? throw new InvalidDataException("The HCA request has no encrypted transaction key.");
        var encryptedOrderData = envelope.EncryptedOrderData
            ?? throw new InvalidDataException("The HCA request has no encrypted order data.");

        var compressed = EncryptionE002.Decrypt(
            new EncryptedOrderData(transactionKey, encryptedOrderData), bankKeys.Encryption, bankKeys.EncryptionVersion);
        var xml = Encoding.UTF8.GetString(EbicsCompression.Decompress(compressed));
        return ParseOrderData(xml);
    }

    /// <summary>
    /// The identifiers and encrypted ciphertexts extracted from an HCA request.
    /// </summary>
    /// <param name="HostId">The raw <c>HostID</c> from the request header.</param>
    /// <param name="PartnerId">The raw <c>PartnerID</c> from the request header.</param>
    /// <param name="UserId">The raw <c>UserID</c> from the request header.</param>
    /// <param name="EncryptedTransactionKey">The RSA-OAEP-encrypted transaction key (<c>DataEncryptionInfo/TransactionKey</c>).</param>
    /// <param name="EncryptedOrderData">The AES-encrypted, compressed order data (<c>DataTransfer/OrderData</c>).</param>
    protected sealed record HcaEnvelope(
        string? HostId,
        string? PartnerId,
        string? UserId,
        byte[]? EncryptedTransactionKey,
        byte[]? EncryptedOrderData);

    /// <summary>
    /// The new authentication/encryption keys extracted from HCA order data. The keys may carry a private
    /// key and are reduced to public-only before storage.
    /// </summary>
    /// <param name="AuthKey">The reconstructed RSA authentication public key.</param>
    /// <param name="AuthVersion">The authentication key version (e.g. <c>X002</c>).</param>
    /// <param name="EncKey">The reconstructed RSA encryption public key.</param>
    /// <param name="EncVersion">The encryption key version (e.g. <c>E002</c>).</param>
    protected sealed record HcaKeys(
        RsaKeyMaterial AuthKey,
        KeyVersion AuthVersion,
        RsaKeyMaterial EncKey,
        KeyVersion EncVersion);
}
