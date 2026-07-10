using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H004;
using S = EBICO.Core.Schema.Signature.S001;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H004 (EBICS 2.5) INI handler. H004 is a <b>pure-key</b> procedure: the signature key travels
/// as an <c>RSAKeyValue</c> (modulus/exponent) in the <c>S001</c> signature namespace.
/// </summary>
public sealed class H004IniOrderHandler : IniOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    public H004IniOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore)
        : base(masterData, keyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H004;

    /// <inheritdoc />
    protected override IniKeyData ExtractIniOrderData(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsUnsecuredRequest
            ?? throw new InvalidDataException("The INI request is not an H004 ebicsUnsecuredRequest.");
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
