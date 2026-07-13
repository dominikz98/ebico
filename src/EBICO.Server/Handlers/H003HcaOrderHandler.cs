using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H003;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H003 (EBICS 2.4) HCA handler. Like H004 it is a <b>pure-key</b> procedure: the new
/// authentication and encryption keys travel as <c>RSAKeyValue</c> (modulus/exponent) elements inside
/// the (E002-encrypted) <c>HCARequestOrderData</c>.
/// </summary>
public sealed class H003HcaOrderHandler : HcaOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    /// <param name="bankKeyStore">The bank key store.</param>
    public H003HcaOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
        : base(masterData, keyStore, bankKeyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    protected override HcaEnvelope ExtractEnvelope(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsRequest
            ?? throw new InvalidDataException("The HCA request is not an H003 ebicsRequest.");
        var header = request.Header?.Static;
        var dataTransfer = request.Body?.DataTransfer;
        return new HcaEnvelope(
            header?.HostId,
            header?.PartnerId,
            header?.UserId,
            dataTransfer?.DataEncryptionInfo?.TransactionKey,
            dataTransfer?.OrderData?.Value);
    }

    /// <inheritdoc />
    protected override HcaKeys ParseOrderData(string orderDataXml)
    {
        var orderData = EbicsXmlSerializer.Deserialize<H.HcaRequestOrderDataType>(orderDataXml);

        var authInfo = orderData.AuthenticationPubKeyInfo
            ?? throw new InvalidDataException("The HCA order data has no AuthenticationPubKeyInfo.");
        var encInfo = orderData.EncryptionPubKeyInfo
            ?? throw new InvalidDataException("The HCA order data has no EncryptionPubKeyInfo.");

        var authRsa = authInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HCA authentication key has no RSAKeyValue.");
        var encRsa = encInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HCA encryption key has no RSAKeyValue.");

        var authKey = RsaKeyImportExport.ImportRsaKeyValue(authRsa.Modulus, authRsa.Exponent);
        var encKey = RsaKeyImportExport.ImportRsaKeyValue(encRsa.Modulus, encRsa.Exponent);

        return new HcaKeys(
            authKey,
            KeyVersion.Create(authInfo.AuthenticationVersion),
            encKey,
            KeyVersion.Create(encInfo.EncryptionVersion));
    }
}
