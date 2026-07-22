using System.Text;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using Ds = EBICO.Core.Schema.XmlDsig;
using H = EBICO.Core.Schema.H003;
using S = EBICO.Core.Schema.Signature.S001;

namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// The H003 (EBICS 2.4) onboarding envelope builder. Like H004 it is a <b>pure-key</b> procedure
/// (public keys as <c>RSAKeyValue</c>, order details with <c>OrderType</c>/<c>OrderAttribute</c>,
/// INI order data in the <c>S001</c> namespace); it differs only in the legacy protocol namespace,
/// which the version bindings apply automatically.
/// </summary>
internal sealed class H003OnboardingEnvelopeBuilder : OnboardingEnvelopeBuilderBase
{
    // ⚠️ Spec-Vorbehalt: OrderAttribute values, as in the H004 builder.
    private const string OrderAttributeUnsecured = "DZNNN";
    private const string OrderAttributeHpb = "DZHNN";

    /// <summary>Initializes the builder.</summary>
    /// <param name="timeProvider">The time source for the HPB timestamp.</param>
    public H003OnboardingEnvelopeBuilder(TimeProvider timeProvider)
        : base(timeProvider)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    protected override object BuildIniOrderData(PublicKeyDescriptor signatureKey, OnboardingHeaderContext ctx)
        => new S.SignaturePubKeyOrderDataType
        {
            SignaturePubKeyInfo = new S.SignaturePubKeyInfoType
            {
                SignatureVersion = signatureKey.KeyVersion,
                PubKeyValue = new S.PubKeyValueType { RsaKeyValue = RsaKeyValue(signatureKey.Key) },
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
                PubKeyValue = new H.PubKeyValueType { RsaKeyValue = RsaKeyValue(authenticationKey.Key) },
            },
            EncryptionPubKeyInfo = new H.EncryptionPubKeyInfoType
            {
                EncryptionVersion = encryptionKey.KeyVersion,
                PubKeyValue = new H.PubKeyValueType { RsaKeyValue = RsaKeyValue(encryptionKey.Key) },
            },
            PartnerId = ctx.PartnerId,
            UserId = ctx.UserId,
        };

    /// <inheritdoc />
    protected override IEbicsRequestEnvelope BuildUnsecuredEnvelope(
        OnboardingHeaderContext ctx, string orderType, byte[] compressedOrderData)
        => new H.EbicsUnsecuredRequest
        {
            Version = "H003",
            Header = new H.EbicsUnsecuredRequestHeader
            {
                Authenticate = true,
                Static = new H.UnsecuredRequestStaticHeaderType
                {
                    HostId = ctx.HostId,
                    PartnerId = ctx.PartnerId,
                    UserId = ctx.UserId,
                    SystemId = ctx.SystemId,
                    // The base type, not UnsecuredReqOrderDetailsType: a derived instance would make
                    // the serializer emit an xsi:type discriminator real clients neither send nor
                    // expect (issue #117, ADR-0029). The restriction adds no members either way.
                    OrderDetails = new H.OrderDetailsType
                    {
                        OrderType = orderType,
                        OrderAttribute = OrderAttributeUnsecured,
                    },
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
            Version = "H003",
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
                    // Base type, see BuildUnsecuredEnvelope above (issue #117, ADR-0029).
                    OrderDetails = new H.OrderDetailsType
                    {
                        OrderType = OrderTypeHpb,
                        OrderAttribute = OrderAttributeHpb,
                    },
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
        return new BankKeys
        {
            Authentication = ImportRsaKey(orderData.AuthenticationPubKeyInfo),
            Encryption = ImportRsaKey(orderData.EncryptionPubKeyInfo),
            HostId = orderData.HostId ?? string.Empty,
        };
    }

    private static Ds.RsaKeyValueType RsaKeyValue(RsaKeyMaterial key)
    {
        var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(key);
        return new Ds.RsaKeyValueType { Modulus = modulus, Exponent = exponent };
    }

    private static RsaKeyMaterial ImportRsaKey(H.PubKeyInfoType? info)
    {
        var rsaKeyValue = info?.PubKeyValue?.RsaKeyValue
            ?? throw new EbicsOnboardingException("The HPB response is missing a bank RSAKeyValue.");
        return RsaKeyImportExport.ImportRsaKeyValue(rsaKeyValue.Modulus, rsaKeyValue.Exponent);
    }
}
