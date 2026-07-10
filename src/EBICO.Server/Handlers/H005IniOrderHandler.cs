using System.Security.Cryptography.X509Certificates;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H005;
using S = EBICO.Core.Schema.Signature.S002;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H005 (EBICS 3.0) INI handler. H005 is <b>certificate-based</b>: the signature key travels as
/// an <c>X509Data</c> certificate in the <c>S002</c> signature namespace. This handler extracts and
/// stores the RSA public key from that certificate.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> only the public key is taken from the certificate; certificate chain /
/// self-signature validation is a conformance concern (M8) and not performed here.
/// </remarks>
public sealed class H005IniOrderHandler : IniOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    public H005IniOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore)
        : base(masterData, keyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    protected override IniKeyData ExtractIniOrderData(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsUnsecuredRequest
            ?? throw new InvalidDataException("The INI request is not an H005 ebicsUnsecuredRequest.");
        var header = request.Header?.Static;
        var orderData = DeserializeOrderData<S.SignaturePubKeyOrderDataType>(
            request.Body?.DataTransfer?.OrderData?.Value);

        var info = orderData.SignaturePubKeyInfo
            ?? throw new InvalidDataException("The INI order data has no SignaturePubKeyInfo.");
        var der = info.X509Data?.X509Certificate.FirstOrDefault()
            ?? throw new InvalidDataException("The INI order data has no X.509 certificate.");

        using var certificate = X509CertificateLoader.LoadCertificate(der);
        var key = RsaKeyImportExport.ImportPublicKeyFromCertificate(certificate);
        var version = KeyVersion.Create(info.SignatureVersion);

        return new IniKeyData(header?.HostId, header?.PartnerId, header?.UserId, key, version);
    }
}
