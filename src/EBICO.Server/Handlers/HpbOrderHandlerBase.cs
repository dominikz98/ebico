using System.Security.Cryptography;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;
using Dsig = EBICO.Core.Schema.XmlDsig;

namespace EBICO.Server.Handlers;

/// <summary>
/// Base for the version-specific HPB order handlers. HPB (<c>OrderType == "HPB"</c>) is the download
/// counterpart of INI/HIA: an onboarded subscriber fetches the <b>bank's</b> public
/// <b>authentication</b> (<c>X00x</c>) and <b>encryption</b> (<c>E00x</c>) keys. Unlike INI/HIA the
/// request is a signed <c>ebicsNoPubKeyDigestsRequest</c> and the response carries an <em>encrypted</em>
/// <c>DataTransfer</c>: the <c>HPBResponseOrderData</c> compressed and E002-encrypted for the
/// subscriber's encryption key (received during HIA), so only the subscriber can read it.
/// </summary>
/// <remarks>
/// <para>
/// The version-agnostic flow lives here: resolve the subscriber (must exist and be
/// <see cref="SubscriberState.Ready"/>), fetch its stored encryption key, obtain the bank key pair
/// from <see cref="IServerBankKeyStore"/>, then compress + encrypt the version-specific order data
/// and compute the recipient-key digest. The version-specific steps are
/// <see cref="ExtractHpbRequest"/> (read the identifiers from the version's request envelope) and
/// <see cref="SerializeBankPubKeyOrderData"/> (build the version's <c>HPBResponseOrderData</c>:
/// H003/H004 as <c>RSAKeyValue</c>, H005 as <c>X509Data</c>).
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the request's X002 authentication signature is <b>not</b> verified and
/// the response is <b>not</b> signed — response/request authentication signatures (X002) are M4,
/// consistent with INI/HIA. Confidentiality still holds because the payload is encrypted for the
/// subscriber's E002 key. HPB requires state <see cref="SubscriberState.Ready"/> (INI + HIA done);
/// there is no lifecycle transition (HPB is read-only). See <c>docs/server/hpb.md</c>.
/// </para>
/// </remarks>
public abstract class HpbOrderHandlerBase : IEbicsOrderHandler
{
    /// <summary>The order type served by HPB handlers.</summary>
    public const string HpbOrderType = "HPB";

    private readonly IMasterDataManager _masterData;
    private readonly IServerKeyStore _keyStore;
    private readonly IServerBankKeyStore _bankKeyStore;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager used to resolve the subscriber.</param>
    /// <param name="keyStore">The server key store the subscriber's encryption key is read from.</param>
    /// <param name="bankKeyStore">The store providing the bank's own key pair.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected HpbOrderHandlerBase(IMasterDataManager masterData, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
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
    public string OrderType => HpbOrderType;

    /// <inheritdoc />
    public async Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        HpbRequestData request;
        try
        {
            request = ExtractHpbRequest(context);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            // The envelope was not the expected no-pub-key-digests request (e.g. a signed ebicsRequest
            // that happens to carry OrderType "HPB").
            return new EbicsOrderResult(EbicsReturnCode.InvalidOrderDataFormat);
        }

        if (!HostId.TryCreate(request.HostId, out var hostId)
            || !PartnerId.TryCreate(request.PartnerId, out var partnerId)
            || !UserId.TryCreate(request.UserId, out var userId))
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // HPB requires a fully onboarded subscriber: INI + HIA done, i.e. state Ready.
        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State != SubscriberState.Ready)
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // The response is encrypted for the subscriber's encryption key (E002), stored during HIA. A
        // Ready subscriber is expected to have one; a missing key is an inconsistent state.
        var keyRef = new SubscriberKeyRef(hostId, partnerId, userId);
        var subscriberEnc = await _keyStore.GetAsync(keyRef, KeyPurpose.Encryption, ct).ConfigureAwait(false);
        if (subscriberEnc is null)
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Build the version-specific HPBResponseOrderData with the bank's public keys, compress it and
        // encrypt it (E002 hybrid) for the subscriber. The transaction key is RSA-OAEP-encrypted with
        // the subscriber's public encryption key; only the subscriber's private key can decrypt it.
        var bankKeys = await _bankKeyStore.GetOrCreateAsync(hostId, ct).ConfigureAwait(false);
        var orderDataXml = SerializeBankPubKeyOrderData(bankKeys, hostId.Value);
        var compressed = EbicsCompression.Compress(orderDataXml);
        var encrypted = EncryptionE002.Encrypt(compressed, subscriberEnc.Key, subscriberEnc.Version);
        var digest = PublicKeyFingerprint.Compute(subscriberEnc.Key);

        var payload = new EbicsKeyManagementPayload(
            encrypted.EncryptedTransactionKey,
            encrypted.EncryptedOrderDataBytes,
            digest,
            subscriberEnc.Version);

        return new EbicsOrderResult(EbicsReturnCode.Ok, payload);
    }

    /// <summary>
    /// Reads the subscriber identifiers out of the version-specific <c>ebicsNoPubKeyDigestsRequest</c>
    /// in <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The request context (its <see cref="EbicsRequestContext.Envelope"/> is the no-pub-key-digests request).</param>
    /// <returns>The extracted identifiers.</returns>
    /// <exception cref="InvalidDataException">The envelope is not the expected no-pub-key-digests request.</exception>
    protected abstract HpbRequestData ExtractHpbRequest(EbicsRequestContext context);

    /// <summary>
    /// Builds the version-specific <c>HPBResponseOrderData</c> document carrying the bank's public
    /// authentication and encryption keys and serializes it to its (uncompressed) XML bytes.
    /// </summary>
    /// <param name="bankKeys">The bank's key pair whose public parts are returned.</param>
    /// <param name="hostId">The host id echoed into the order data.</param>
    /// <returns>The serialized order-data XML (before compression/encryption).</returns>
    protected abstract byte[] SerializeBankPubKeyOrderData(BankKeyPair bankKeys, string hostId);

    /// <summary>
    /// Converts RSA key material to an XML-DSig <c>RSAKeyValue</c> (modulus/exponent) for the
    /// pure-key protocol versions (H003/H004).
    /// </summary>
    /// <param name="key">The public key material to render.</param>
    /// <returns>The <c>RSAKeyValue</c> element.</returns>
    protected static Dsig.RsaKeyValueType ToRsaKeyValue(RsaKeyMaterial key)
    {
        var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(key);
        return new Dsig.RsaKeyValueType { Modulus = modulus, Exponent = exponent };
    }

    /// <summary>Wraps a DER-encoded certificate in an XML-DSig <c>X509Data</c> element (H005).</summary>
    /// <param name="der">The DER-encoded certificate bytes.</param>
    /// <returns>The <c>X509Data</c> element.</returns>
    protected static Dsig.X509DataType ToX509Data(byte[] der)
    {
        var data = new Dsig.X509DataType();
        data.X509Certificate.Add(der);
        return data;
    }

    /// <summary>
    /// The subscriber identifiers extracted from an HPB request. Identifiers are the raw header
    /// strings (validated later by the base handler).
    /// </summary>
    /// <param name="HostId">The raw <c>HostID</c> from the request header.</param>
    /// <param name="PartnerId">The raw <c>PartnerID</c> from the request header.</param>
    /// <param name="UserId">The raw <c>UserID</c> from the request header.</param>
    protected sealed record HpbRequestData(string? HostId, string? PartnerId, string? UserId);
}
