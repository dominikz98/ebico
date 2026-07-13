using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H003;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H003 (EBICS 2.4) HCS handler. Like H004 it is a <b>pure-key</b> procedure: the new signature
/// (<c>S001</c>), authentication and encryption keys travel as <c>RSAKeyValue</c> (modulus/exponent)
/// elements inside the (E002-encrypted) <c>HCSRequestOrderData</c>.
/// </summary>
public sealed class H003HcsOrderHandler : HcsOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    /// <param name="bankKeyStore">The bank key store.</param>
    public H003HcsOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
        : base(masterData, keyStore, bankKeyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    protected override HcsEnvelope ExtractEnvelope(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsRequest
            ?? throw new InvalidDataException("The HCS request is not an H003 ebicsRequest.");
        var header = request.Header?.Static;
        var dataTransfer = request.Body?.DataTransfer;
        return new HcsEnvelope(
            header?.HostId,
            header?.PartnerId,
            header?.UserId,
            dataTransfer?.DataEncryptionInfo?.TransactionKey,
            dataTransfer?.OrderData?.Value);
    }

    /// <inheritdoc />
    protected override HcsKeys ParseOrderData(string orderDataXml)
    {
        var orderData = EbicsXmlSerializer.Deserialize<H.HcsRequestOrderDataType>(orderDataXml);

        var sigInfo = orderData.SignaturePubKeyInfo
            ?? throw new InvalidDataException("The HCS order data has no SignaturePubKeyInfo.");
        var authInfo = orderData.AuthenticationPubKeyInfo
            ?? throw new InvalidDataException("The HCS order data has no AuthenticationPubKeyInfo.");
        var encInfo = orderData.EncryptionPubKeyInfo
            ?? throw new InvalidDataException("The HCS order data has no EncryptionPubKeyInfo.");

        var sigRsa = sigInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HCS signature key has no RSAKeyValue.");
        var authRsa = authInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HCS authentication key has no RSAKeyValue.");
        var encRsa = encInfo.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The HCS encryption key has no RSAKeyValue.");

        var sigKey = RsaKeyImportExport.ImportRsaKeyValue(sigRsa.Modulus, sigRsa.Exponent);
        var authKey = RsaKeyImportExport.ImportRsaKeyValue(authRsa.Modulus, authRsa.Exponent);
        var encKey = RsaKeyImportExport.ImportRsaKeyValue(encRsa.Modulus, encRsa.Exponent);

        return new HcsKeys(
            sigKey,
            KeyVersion.Create(sigInfo.SignatureVersion),
            authKey,
            KeyVersion.Create(authInfo.AuthenticationVersion),
            encKey,
            KeyVersion.Create(encInfo.EncryptionVersion));
    }
}
