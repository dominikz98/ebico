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
/// Base for the version-specific HCS order handlers. HCS (<c>OrderType == "HCS"</c>) is the key-change
/// order that replaces <b>all</b> of an already-onboarded subscriber's keys: the bank-technical
/// <b>signature</b> key (<c>A00x</c>), the <b>authentication</b> key (<c>X00x</c>) and the
/// <b>encryption</b> key (<c>E00x</c>). It is the combination of INI + HIA in a single key change and,
/// like HCA, arrives as a signed <c>ebicsRequest</c> whose order data is E002-encrypted for the bank's
/// public encryption key. The subscriber stays <see cref="SubscriberState.Ready"/>.
/// </summary>
/// <remarks>
/// <para>
/// The version-agnostic flow mirrors <see cref="HcaOrderHandlerBase"/> but replaces three keys instead
/// of two: read the identifiers and the encrypted order data (<see cref="ExtractEnvelope"/>), fetch the
/// bank key pair, E002-decrypt + decompress, extract the three new keys (<see cref="ParseOrderData"/>),
/// enforce the key-version policy, require the subscriber to exist and be
/// <see cref="SubscriberState.Ready"/>, then upsert all three keys (the store replaces per purpose).
/// The signature key travels in the <c>S001</c> namespace for H003/H004 and <c>S002</c> for H005.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> as with HCA the order data is only <em>decrypted</em>, not authenticated —
/// the order signature and the X002 authentication signature are <b>not</b> verified (M4, consistent
/// with INI/HIA/HPB). See <c>docs/server/hca-hcs-spr-hsa.md</c>.
/// </para>
/// </remarks>
public abstract class HcsOrderHandlerBase : IEbicsOrderHandler
{
    /// <summary>The order type served by HCS handlers.</summary>
    public const string HcsOrderType = "HCS";

    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;
    private readonly IServerBankKeyStore _bankKeyStore;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager used to resolve the subscriber.</param>
    /// <param name="keyStore">The server key store the new signature, authentication and encryption keys are written to.</param>
    /// <param name="bankKeyStore">The store providing the bank's own key pair (its private encryption key decrypts the order data).</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected HcsOrderHandlerBase(IMasterDataManager masterData, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
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
    public string OrderType => HcsOrderType;

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

        var bankKeys = await _bankKeyStore.GetOrCreateAsync(hostId, ct).ConfigureAwait(false);

        // Decrypt and parse the order data; any decode/validation fault surfaces as
        // EbicsOrderDataException -> InvalidOrderDataFormat.
        var keys = OrderDataFault.Wrap(() =>
        {
            var parsed = DecryptAndParse(envelope, bankKeys);

            if (parsed.SigVersion.Purpose != KeyPurpose.Signature
                || parsed.AuthVersion.Purpose != KeyPurpose.Authentication
                || parsed.EncVersion.Purpose != KeyPurpose.Encryption)
            {
                throw new EbicsOrderDataException("The HCS order carries a purpose-mismatched key version.");
            }

            _ = KeyVersions.EnsurePermitted(parsed.SigVersion, Version);
            _ = KeyVersions.EnsurePermitted(parsed.AuthVersion, Version);
            _ = KeyVersions.EnsurePermitted(parsed.EncVersion, Version);
            return parsed;
        });

        // HCS requires a fully onboarded subscriber (INI + HIA done). The key change keeps state Ready.
        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.Ready)
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Replace all three keys (the store upserts per purpose).
        var keyRef = new SubscriberKeyRef(hostId, partnerId, userId);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(keys.SigKey.ToPublicOnly(), keys.SigVersion), ct).ConfigureAwait(false);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(keys.AuthKey.ToPublicOnly(), keys.AuthVersion), ct).ConfigureAwait(false);
        await _keyStore.StoreAsync(keyRef, new StoredPublicKey(keys.EncKey.ToPublicOnly(), keys.EncVersion), ct).ConfigureAwait(false);

        return new EbicsOrderResult(EbicsReturnCode.Ok);
    }

    /// <summary>
    /// Reads the subscriber identifiers and the encrypted order-data ciphertexts out of the
    /// version-specific <c>ebicsRequest</c>.
    /// </summary>
    /// <param name="context">The request context (its <see cref="EbicsRequestContext.Envelope"/> is the signed request).</param>
    /// <returns>The extracted identifiers and ciphertexts.</returns>
    /// <exception cref="InvalidDataException">The envelope is not the expected signed request.</exception>
    protected abstract HcsEnvelope ExtractEnvelope(EbicsRequestContext context);

    /// <summary>
    /// Deserializes the decrypted, decompressed order-data XML and extracts the new signature,
    /// authentication and encryption keys (H003/H004 read <c>RSAKeyValue</c>, H005 reads <c>X509Data</c>).
    /// </summary>
    /// <param name="orderDataXml">The decrypted and decompressed order-data XML.</param>
    /// <returns>The extracted keys and their versions.</returns>
    /// <remarks>Implementations may throw the low-level order-data failures wrapped by <see cref="OrderDataFault"/> into <see cref="EbicsOrderDataException"/> (mapped to <see cref="EbicsReturnCode.InvalidOrderDataFormat"/>).</remarks>
    protected abstract HcsKeys ParseOrderData(string orderDataXml);

    private HcsKeys DecryptAndParse(HcsEnvelope envelope, BankKeyPair bankKeys)
    {
        var transactionKey = envelope.EncryptedTransactionKey
            ?? throw new InvalidDataException("The HCS request has no encrypted transaction key.");
        var encryptedOrderData = envelope.EncryptedOrderData
            ?? throw new InvalidDataException("The HCS request has no encrypted order data.");

        var compressed = EncryptionE002.Decrypt(
            new EncryptedOrderData(transactionKey, encryptedOrderData), bankKeys.Encryption, bankKeys.EncryptionVersion);
        var xml = Encoding.UTF8.GetString(EbicsCompression.Decompress(compressed));
        return ParseOrderData(xml);
    }

    /// <summary>The identifiers and encrypted ciphertexts extracted from an HCS request.</summary>
    /// <param name="HostId">The raw <c>HostID</c> from the request header.</param>
    /// <param name="PartnerId">The raw <c>PartnerID</c> from the request header.</param>
    /// <param name="UserId">The raw <c>UserID</c> from the request header.</param>
    /// <param name="EncryptedTransactionKey">The RSA-OAEP-encrypted transaction key (<c>DataEncryptionInfo/TransactionKey</c>).</param>
    /// <param name="EncryptedOrderData">The AES-encrypted, compressed order data (<c>DataTransfer/OrderData</c>).</param>
    protected sealed record HcsEnvelope(
        string? HostId,
        string? PartnerId,
        string? UserId,
        byte[]? EncryptedTransactionKey,
        byte[]? EncryptedOrderData);

    /// <summary>
    /// The new signature/authentication/encryption keys extracted from HCS order data. The keys may
    /// carry a private key and are reduced to public-only before storage.
    /// </summary>
    /// <param name="SigKey">The reconstructed RSA bank-technical signature public key.</param>
    /// <param name="SigVersion">The signature key version (e.g. <c>A005</c>/<c>A006</c>).</param>
    /// <param name="AuthKey">The reconstructed RSA authentication public key.</param>
    /// <param name="AuthVersion">The authentication key version (e.g. <c>X002</c>).</param>
    /// <param name="EncKey">The reconstructed RSA encryption public key.</param>
    /// <param name="EncVersion">The encryption key version (e.g. <c>E002</c>).</param>
    protected sealed record HcsKeys(
        RsaKeyMaterial SigKey,
        KeyVersion SigVersion,
        RsaKeyMaterial AuthKey,
        KeyVersion AuthVersion,
        RsaKeyMaterial EncKey,
        KeyVersion EncVersion);
}
