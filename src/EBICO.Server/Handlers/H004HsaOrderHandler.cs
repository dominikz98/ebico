using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H004;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H004 (EBICS 2.5) HSA handler. Like H003 it is a <b>pure-key</b> procedure: the authentication
/// and encryption keys travel as <c>RSAKeyValue</c> (modulus/exponent) elements inside the
/// <c>HSARequestOrderData</c>.
/// </summary>
public sealed class H004HsaOrderHandler : HsaOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    public H004HsaOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore)
        : base(masterData, keyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H004;

    /// <inheritdoc />
    protected override HsaKeyData ExtractHsaOrderData(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsUnsecuredRequest
            ?? throw new InvalidDataException("The HSA request is not an H004 ebicsUnsecuredRequest.");
        var header = request.Header?.Static;
        var orderData = DeserializeOrderData<H.HsaRequestOrderDataType>(
            request.Body?.DataTransfer?.OrderData?.Value);

        var authInfo = orderData.AuthenticationPubKeyInfo
            ?? throw new InvalidDataException("The HSA order data has no AuthenticationPubKeyInfo.");
        var encInfo = orderData.EncryptionPubKeyInfo
            ?? throw new InvalidDataException("The HSA order data has no EncryptionPubKeyInfo.");

        var authRsa = authInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HSA authentication key has no RSAKeyValue.");
        var encRsa = encInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HSA encryption key has no RSAKeyValue.");

        var authKey = RsaKeyImportExport.ImportRsaKeyValue(authRsa.Modulus, authRsa.Exponent);
        var encKey = RsaKeyImportExport.ImportRsaKeyValue(encRsa.Modulus, encRsa.Exponent);

        return new HsaKeyData(
            header?.HostId,
            header?.PartnerId,
            header?.UserId,
            authKey,
            KeyVersion.Create(authInfo.AuthenticationVersion),
            encKey,
            KeyVersion.Create(encInfo.EncryptionVersion));
    }
}
