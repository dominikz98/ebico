using System.Security.Cryptography.X509Certificates;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H005;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H005 (EBICS 3.0) HCA handler. H005 is <b>certificate-based</b>: the new authentication and
/// encryption keys travel as <c>X509Data</c> certificates inside the (E002-encrypted)
/// <c>HCARequestOrderData</c>. This handler extracts and stores the RSA public keys from those
/// certificates.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> only the public key is taken from each certificate; certificate chain /
/// self-signature validation is a conformance concern (M8) and not performed here.
/// </remarks>
public sealed class H005HcaOrderHandler : HcaOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    /// <param name="bankKeyStore">The bank key store.</param>
    public H005HcaOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore, IServerBankKeyStore bankKeyStore)
        : base(masterData, keyStore, bankKeyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    protected override HcaEnvelope ExtractEnvelope(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsRequest
            ?? throw new InvalidDataException("The HCA request is not an H005 ebicsRequest.");
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

        var authKey = ImportFromCertificate(authInfo.X509Data?.X509Certificate.FirstOrDefault(), "authentication");
        var encKey = ImportFromCertificate(encInfo.X509Data?.X509Certificate.FirstOrDefault(), "encryption");

        return new HcaKeys(
            authKey,
            KeyVersion.Create(authInfo.AuthenticationVersion),
            encKey,
            KeyVersion.Create(encInfo.EncryptionVersion));
    }

    private static RsaKeyMaterial ImportFromCertificate(byte[]? der, string role)
    {
        var raw = der ?? throw new InvalidDataException($"The HCA order data has no X.509 {role} certificate.");
        using var certificate = X509CertificateLoader.LoadCertificate(raw);
        return RsaKeyImportExport.ImportPublicKeyFromCertificate(certificate);
    }
}
