using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H003;
using S = EBICO.Core.Schema.Signature.S001;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H003 (EBICS 2.4) INI handler. Like H004 it is a <b>pure-key</b> procedure: the signature key
/// travels as an <c>RSAKeyValue</c> (modulus/exponent) in the <c>S001</c> signature namespace.
/// </summary>
public sealed class H003IniOrderHandler : IniOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    public H003IniOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore)
        : base(masterData, keyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    protected override IniKeyData ExtractIniOrderData(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsUnsecuredRequest
            ?? throw new InvalidDataException("The INI request is not an H003 ebicsUnsecuredRequest.");
        var header = request.Header?.Static;
        var orderData = DeserializeOrderData<S.SignaturePubKeyOrderDataType>(
            request.Body?.DataTransfer?.OrderData?.Value);

        var info = orderData.SignaturePubKeyInfo
            ?? throw new InvalidDataException("The INI order data has no SignaturePubKeyInfo.");
        var rsaKeyValue = info.PubKeyValue?.RsaKeyValue
            ?? throw new InvalidDataException("The INI order data has no RSAKeyValue.");

        var key = RsaKeyImportExport.ImportRsaKeyValue(rsaKeyValue.Modulus, rsaKeyValue.Exponent);
        var version = KeyVersion.Create(info.SignatureVersion);

        return new IniKeyData(header?.HostId, header?.PartnerId, header?.UserId, key, version);
    }
}
