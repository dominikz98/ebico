using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using S001 = EBICO.Core.Schema.Signature.S001;
using S002 = EBICO.Core.Schema.Signature.S002;

namespace EBICO.Connector.Upload;

/// <summary>
/// Assembles the EBICS electronic signature (ES) blob for an upload: computes the bank-technical
/// (authorising) signature over the order data with the subscriber's A00x key and wraps it in the
/// version-appropriate <c>UserSignatureData</c> document — <c>S001</c> for H003/H004, <c>S002</c> for
/// H005 — serialized to bytes (ready to compress + encrypt into <c>DataTransfer/SignatureData</c>).
/// </summary>
internal static class UserSignatureDataAssembler
{
    /// <summary>
    /// Builds the serialized <c>UserSignatureData</c> carrying one <c>OrderSignatureData</c> for the
    /// given order data.
    /// </summary>
    /// <param name="version">The EBICS version (selects the S001/S002 signature namespace).</param>
    /// <param name="orderData">The order data the signature is computed over.</param>
    /// <param name="signatureKey">The subscriber's A00x signature key (must contain a private key).</param>
    /// <param name="signatureVersion">The signature key version (e.g. <c>A005</c>/<c>A006</c>).</param>
    /// <param name="partnerId">The signer's <c>PartnerID</c>.</param>
    /// <param name="userId">The signer's <c>UserID</c>.</param>
    /// <returns>The serialized <c>UserSignatureData</c> XML bytes.</returns>
    public static byte[] Build(
        EbicsVersion version,
        ReadOnlySpan<byte> orderData,
        RsaKeyMaterial signatureKey,
        KeyVersion signatureVersion,
        string partnerId,
        string userId)
    {
        var signatureValue = BankSignature.Sign(orderData, signatureKey, signatureVersion);

        if (version == EbicsVersion.H005)
        {
            var userSignature = new S002.UserSignatureDataSigBookType();
            userSignature.OrderSignatureData.Add(new S002.OrderSignatureDataType
            {
                SignatureVersion = signatureVersion.Value,
                SignatureValue = signatureValue,
                PartnerId = partnerId,
                UserId = userId,
            });
            return EbicsXmlSerializer.SerializeOrderData(userSignature);
        }

        var legacySignature = new S001.UserSignatureDataSigBookType();
        legacySignature.OrderSignatureData.Add(new S001.OrderSignatureDataType
        {
            SignatureVersion = signatureVersion.Value,
            SignatureValue = signatureValue,
            PartnerId = partnerId,
            UserId = userId,
        });
        return EbicsXmlSerializer.SerializeOrderData(legacySignature);
    }
}
