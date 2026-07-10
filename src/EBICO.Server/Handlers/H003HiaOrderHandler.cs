using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H003;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H003 (EBICS 2.4) HIA handler. Like H004 it is a <b>pure-key</b> procedure: the authentication
/// and encryption keys travel as <c>RSAKeyValue</c> (modulus/exponent) elements inside the
/// <c>HIARequestOrderData</c>.
/// </summary>
public sealed class H003HiaOrderHandler : HiaOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    public H003HiaOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore)
        : base(masterData, keyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    protected override HiaKeyData ExtractHiaOrderData(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsUnsecuredRequest
            ?? throw new InvalidDataException("The HIA request is not an H003 ebicsUnsecuredRequest.");
        var header = request.Header?.Static;
        var orderData = DeserializeOrderData<H.HiaRequestOrderDataType>(
            request.Body?.DataTransfer?.OrderData?.Value);

        var authInfo = orderData.AuthenticationPubKeyInfo
            ?? throw new InvalidDataException("The HIA order data has no AuthenticationPubKeyInfo.");
        var encInfo = orderData.EncryptionPubKeyInfo
            ?? throw new InvalidDataException("The HIA order data has no EncryptionPubKeyInfo.");

        var authRsa = authInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HIA authentication key has no RSAKeyValue.");
        var encRsa = encInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HIA encryption key has no RSAKeyValue.");

        var authKey = RsaKeyImportExport.ImportRsaKeyValue(authRsa.Modulus, authRsa.Exponent);
        var encKey = RsaKeyImportExport.ImportRsaKeyValue(encRsa.Modulus, encRsa.Exponent);

        return new HiaKeyData(
            header?.HostId,
            header?.PartnerId,
            header?.UserId,
            authKey,
            KeyVersion.Create(authInfo.AuthenticationVersion),
            encKey,
            KeyVersion.Create(encInfo.EncryptionVersion));
    }
}
