using System.Security.Cryptography.X509Certificates;
using System.Text;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using Ds = EBICO.Core.Schema.XmlDsig;
using H = EBICO.Core.Schema.H005;
using S = EBICO.Core.Schema.Signature.S002;

namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// The H005 (EBICS 3.0) onboarding envelope builder. H005 is <b>certificate-based</b>: public keys
/// travel as <c>X509Data</c> (no <c>RSAKeyValue</c>), and order details use <c>AdminOrderType</c>.
/// The INI order data lives in the <c>S002</c> signature namespace.
/// </summary>
internal sealed class H005OnboardingEnvelopeBuilder : OnboardingEnvelopeBuilderBase
{
    /// <summary>Initializes the builder.</summary>
    /// <param name="timeProvider">The time source for the HPB timestamp.</param>
    public H005OnboardingEnvelopeBuilder(TimeProvider timeProvider)
        : base(timeProvider)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    protected override object BuildIniOrderData(PublicKeyDescriptor signatureKey, OnboardingHeaderContext ctx)
        => new S.SignaturePubKeyOrderDataType
        {
            SignaturePubKeyInfo = new S.SignaturePubKeyInfoType
            {
                SignatureVersion = signatureKey.KeyVersion,
                X509Data = CertificateData(signatureKey),
            },
            PartnerId = ctx.PartnerId,
            UserId = ctx.UserId,
        };

    /// <inheritdoc />
    protected override object BuildHiaOrderData(
        PublicKeyDescriptor authenticationKey, PublicKeyDescriptor encryptionKey, OnboardingHeaderContext ctx)
        => new H.HiaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H.AuthenticationPubKeyInfoType
            {
                AuthenticationVersion = authenticationKey.KeyVersion,
                X509Data = CertificateData(authenticationKey),
            },
            EncryptionPubKeyInfo = new H.EncryptionPubKeyInfoType
            {
                EncryptionVersion = encryptionKey.KeyVersion,
                X509Data = CertificateData(encryptionKey),
            },
            PartnerId = ctx.PartnerId,
            UserId = ctx.UserId,
        };

    /// <inheritdoc />
    protected override IEbicsRequestEnvelope BuildUnsecuredEnvelope(
        OnboardingHeaderContext ctx, string orderType, byte[] compressedOrderData)
        => new H.EbicsUnsecuredRequest
        {
            Version = "H005",
            Header = new H.EbicsUnsecuredRequestHeader
            {
                Authenticate = true,
                Static = new H.UnsecuredRequestStaticHeaderType
                {
                    HostId = ctx.HostId,
                    PartnerId = ctx.PartnerId,
                    UserId = ctx.UserId,
                    SystemId = ctx.SystemId,
                    OrderDetails = new H.UnsecuredReqOrderDetailsType { AdminOrderType = orderType },
                    SecurityMedium = SecurityMedium,
                },
                Mutable = new H.EmptyMutableHeaderType(),
            },
            Body = new H.EbicsUnsecuredRequestBody
            {
                DataTransfer = new H.EbicsUnsecuredRequestBodyDataTransfer
                {
                    OrderData = new H.EbicsUnsecuredRequestBodyDataTransferOrderData { Value = compressedOrderData },
                },
            },
        };

    /// <inheritdoc />
    protected override IAuthSignedRequestEnvelope BuildHpbEnvelope(
        OnboardingHeaderContext ctx, byte[] nonce, DateTime timestamp)
        => new H.EbicsNoPubKeyDigestsRequest
        {
            Version = "H005",
            Header = new H.EbicsNoPubKeyDigestsRequestHeader
            {
                Authenticate = true,
                Static = new H.NoPubKeyDigestsRequestStaticHeaderType
                {
                    HostId = ctx.HostId,
                    Nonce = nonce,
                    Timestamp = timestamp,
                    PartnerId = ctx.PartnerId,
                    UserId = ctx.UserId,
                    SystemId = ctx.SystemId,
                    OrderDetails = new H.NoPubKeyDigestsReqOrderDetailsType { AdminOrderType = OrderTypeHpb },
                    SecurityMedium = SecurityMedium,
                },
                Mutable = new H.EmptyMutableHeaderType(),
            },
            Body = new H.EbicsNoPubKeyDigestsRequestBody(),
        };

    /// <inheritdoc />
    public override KeyManagementResponseView ParseResponse(string responseXml)
    {
        var response = EbicsXmlSerializer.Deserialize<H.EbicsKeyManagementResponse>(responseXml);
        var dataTransfer = response.Body?.DataTransfer;
        return new KeyManagementResponseView
        {
            ReturnCode = response.Body?.ReturnCode?.Value ?? response.Header?.Mutable?.ReturnCode ?? string.Empty,
            ReportText = response.Header?.Mutable?.ReportText,
            EncryptedTransactionKey = dataTransfer?.DataEncryptionInfo?.TransactionKey,
            EncryptedOrderData = dataTransfer?.OrderData?.Value,
        };
    }

    /// <inheritdoc />
    public override BankKeys ParseHpbOrderData(byte[] hpbOrderDataXml)
    {
        var orderData = EbicsXmlSerializer.Deserialize<H.HpbResponseOrderDataType>(Encoding.UTF8.GetString(hpbOrderDataXml));
        var authCertificate = LoadCertificate(orderData.AuthenticationPubKeyInfo?.X509Data);
        var encCertificate = LoadCertificate(orderData.EncryptionPubKeyInfo?.X509Data);
        return new BankKeys
        {
            Authentication = RsaKeyImportExport.ImportPublicKeyFromCertificate(authCertificate),
            Encryption = RsaKeyImportExport.ImportPublicKeyFromCertificate(encCertificate),
            AuthenticationCertificate = authCertificate,
            EncryptionCertificate = encCertificate,
            HostId = orderData.HostId ?? string.Empty,
        };
    }

    private static Ds.X509DataType CertificateData(PublicKeyDescriptor descriptor)
    {
        if (descriptor.Certificate is null)
        {
            throw new EbicsConfigurationException(
                "H005 onboarding requires an X.509 certificate for each key; the key descriptor has none. " +
                "Generate subscriber keys with certificate support before sending INI/HIA on H005.");
        }

        var data = new Ds.X509DataType();
        data.X509Certificate.Add(descriptor.Certificate.RawData);
        return data;
    }

    private static X509Certificate2 LoadCertificate(Ds.X509DataType? x509Data)
    {
        var der = x509Data?.X509Certificate.FirstOrDefault()
            ?? throw new EbicsOnboardingException("The HPB response is missing a bank X.509 certificate.");
        return X509CertificateLoader.LoadCertificate(der);
    }
}
