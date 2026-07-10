using System.Security.Cryptography.X509Certificates;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H005;

namespace EBICO.Server.Handlers;

/// <summary>
/// The H005 (EBICS 3.0) HIA handler. H005 is <b>certificate-based</b>: the authentication and
/// encryption keys travel as <c>X509Data</c> certificates inside the <c>HIARequestOrderData</c>. This
/// handler extracts and stores the RSA public keys from those certificates.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> only the public key is taken from each certificate; certificate chain /
/// self-signature validation is a conformance concern (M8) and not performed here.
/// </remarks>
public sealed class H005HiaOrderHandler : HiaOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    /// <param name="keyStore">The server key store.</param>
    public H005HiaOrderHandler(IMasterDataManager masterData, IServerKeyStore keyStore)
        : base(masterData, keyStore)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    protected override HiaKeyData ExtractHiaOrderData(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsUnsecuredRequest
            ?? throw new InvalidDataException("The HIA request is not an H005 ebicsUnsecuredRequest.");
        var header = request.Header?.Static;
        var orderData = DeserializeOrderData<H.HiaRequestOrderDataType>(
            request.Body?.DataTransfer?.OrderData?.Value);

        var authInfo = orderData.AuthenticationPubKeyInfo
            ?? throw new InvalidDataException("The HIA order data has no AuthenticationPubKeyInfo.");
        var encInfo = orderData.EncryptionPubKeyInfo
            ?? throw new InvalidDataException("The HIA order data has no EncryptionPubKeyInfo.");

        var authKey = ImportFromCertificate(authInfo.X509Data?.X509Certificate.FirstOrDefault(), "authentication");
        var encKey = ImportFromCertificate(encInfo.X509Data?.X509Certificate.FirstOrDefault(), "encryption");

        return new HiaKeyData(
            header?.HostId,
            header?.PartnerId,
            header?.UserId,
            authKey,
            KeyVersion.Create(authInfo.AuthenticationVersion),
            encKey,
            KeyVersion.Create(encInfo.EncryptionVersion));
    }

    private static RsaKeyMaterial ImportFromCertificate(byte[]? der, string role)
    {
        var raw = der ?? throw new InvalidDataException($"The HIA order data has no X.509 {role} certificate.");
        using var certificate = X509CertificateLoader.LoadCertificate(raw);
        return RsaKeyImportExport.ImportPublicKeyFromCertificate(certificate);
    }
}
