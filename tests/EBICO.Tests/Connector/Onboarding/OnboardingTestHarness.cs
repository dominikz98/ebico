using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text;
using H3 = EBICO.Core.Schema.H003;
using H4 = EBICO.Core.Schema.H004;
using H5 = EBICO.Core.Schema.H005;
using S1 = EBICO.Core.Schema.Signature.S001;
using S2 = EBICO.Core.Schema.Signature.S002;

namespace EBICO.Tests.Connector.Onboarding;

/// <summary>
/// Test harness for the onboarding handlers: wires a connector service provider with a
/// <see cref="FakeTransport"/> and a fixed clock, and builds the simulated bank
/// <c>ebicsKeyManagementResponse</c> payloads (success/failure and the encrypted HPB response) for
/// each EBICS version — i.e. it stands in for the not-yet-built server side (Tier-A).
/// </summary>
internal static class OnboardingTestHarness
{
    /// <summary>The fixed instant used by the harness clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a service provider for <paramref name="version"/> with the given transport and a fixed clock.</summary>
    public static ServiceProvider BuildProvider(EbicsVersion version, ITransport transport)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddEbicoConnector(o =>
        {
            o.Url = "https://bank.example/ebics";
            o.HostId = "HOST";
            o.PartnerId = "PART";
            o.UserId = "USER";
            o.Version = version;
        });
        services.AddEbicoOnboarding();
        services.RemoveAll<ITransport>();
        services.AddSingleton(transport);
        return services.BuildServiceProvider();
    }

    /// <summary>Builds a key-management response with only a return code (as INI/HIA responses have).</summary>
    public static byte[] KeyManagementResponse(EbicsVersion version, string returnCode) => version switch
    {
        EbicsVersion.H003 => EbicsXmlSerializer.SerializeToUtf8Bytes(
            new H3.EbicsKeyManagementResponse
            {
                Version = "H003",
                Header = new H3.EbicsKeyManagementResponseHeader
                {
                    Static = new H3.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H3.KeyMgmntResponseMutableHeaderType { ReturnCode = returnCode, ReportText = "OK" },
                },
                Body = new H3.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H3.EbicsKeyManagementResponseBodyReturnCode { Value = returnCode },
                },
            },
            version),
        EbicsVersion.H004 => EbicsXmlSerializer.SerializeToUtf8Bytes(
            new H4.EbicsKeyManagementResponse
            {
                Version = "H004",
                Header = new H4.EbicsKeyManagementResponseHeader
                {
                    Static = new H4.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H4.KeyMgmntResponseMutableHeaderType { ReturnCode = returnCode, ReportText = "OK" },
                },
                Body = new H4.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H4.EbicsKeyManagementResponseBodyReturnCode { Value = returnCode },
                },
            },
            version),
        _ => EbicsXmlSerializer.SerializeToUtf8Bytes(
            new H5.EbicsKeyManagementResponse
            {
                Version = "H005",
                Header = new H5.EbicsKeyManagementResponseHeader
                {
                    Static = new H5.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H5.KeyMgmntResponseMutableHeaderType { ReturnCode = returnCode, ReportText = "OK" },
                },
                Body = new H5.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H5.EbicsKeyManagementResponseBodyReturnCode { Value = returnCode },
                },
            },
            version),
    };

    /// <summary>
    /// Builds a successful HPB response: encrypts the (compressed) HPB order data for
    /// <paramref name="subscriberEncryptionKey"/> and wraps it in the version's key-management response.
    /// </summary>
    public static byte[] HpbResponse(
        EbicsVersion version,
        RsaKeyMaterial subscriberEncryptionKey,
        KeyVersion subscriberEncryptionVersion,
        RsaKeyMaterial bankAuthenticationKey,
        RsaKeyMaterial bankEncryptionKey,
        string bankAuthenticationVersion,
        string bankEncryptionVersion,
        string hostId)
    {
        var orderDataXml = HpbOrderData(
            version, bankAuthenticationKey, bankEncryptionKey, bankAuthenticationVersion, bankEncryptionVersion, hostId);
        var compressed = EbicsCompression.Compress(orderDataXml);
        var encrypted = EncryptionE002.Encrypt(compressed, subscriberEncryptionKey, subscriberEncryptionVersion);
        return WrapHpbResponse(version, encrypted.EncryptedTransactionKey, encrypted.EncryptedOrderDataBytes);
    }

    private static byte[] HpbOrderData(
        EbicsVersion version,
        RsaKeyMaterial bankAuth,
        RsaKeyMaterial bankEnc,
        string authVersion,
        string encVersion,
        string hostId)
    {
        switch (version)
        {
            case EbicsVersion.H005:
                using (var authCert = SelfSignedCertificateFactory.Create(bankAuth, KeyPurpose.Authentication, "CN=Bank", Now.AddMinutes(-5), Now.AddYears(1)))
                using (var encCert = SelfSignedCertificateFactory.Create(bankEnc, KeyPurpose.Encryption, "CN=Bank", Now.AddMinutes(-5), Now.AddYears(1)))
                {
                    var auth = new H5.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, X509Data = X509(authCert.RawData) };
                    var enc = new H5.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, X509Data = X509(encCert.RawData) };
                    return EbicsXmlSerializer.SerializeOrderData(new H5.HpbResponseOrderDataType
                    {
                        AuthenticationPubKeyInfo = auth,
                        EncryptionPubKeyInfo = enc,
                        HostId = hostId,
                    });
                }

            case EbicsVersion.H004:
                return EbicsXmlSerializer.SerializeOrderData(new H4.HpbResponseOrderDataType
                {
                    AuthenticationPubKeyInfo = new H4.AuthenticationPubKeyInfoType
                    {
                        AuthenticationVersion = authVersion,
                        PubKeyValue = new H4.PubKeyValueType { RsaKeyValue = RsaKeyValue(bankAuth) },
                    },
                    EncryptionPubKeyInfo = new H4.EncryptionPubKeyInfoType
                    {
                        EncryptionVersion = encVersion,
                        PubKeyValue = new H4.PubKeyValueType { RsaKeyValue = RsaKeyValue(bankEnc) },
                    },
                    HostId = hostId,
                });

            default:
                return EbicsXmlSerializer.SerializeOrderData(new H3.HpbResponseOrderDataType
                {
                    AuthenticationPubKeyInfo = new H3.AuthenticationPubKeyInfoType
                    {
                        AuthenticationVersion = authVersion,
                        PubKeyValue = new H3.PubKeyValueType { RsaKeyValue = RsaKeyValue(bankAuth) },
                    },
                    EncryptionPubKeyInfo = new H3.EncryptionPubKeyInfoType
                    {
                        EncryptionVersion = encVersion,
                        PubKeyValue = new H3.PubKeyValueType { RsaKeyValue = RsaKeyValue(bankEnc) },
                    },
                    HostId = hostId,
                });
        }
    }

    private static byte[] WrapHpbResponse(EbicsVersion version, byte[] transactionKey, byte[] orderData) => version switch
    {
        EbicsVersion.H003 => EbicsXmlSerializer.SerializeToUtf8Bytes(
            new H3.EbicsKeyManagementResponse
            {
                Version = "H003",
                Header = new H3.EbicsKeyManagementResponseHeader
                {
                    Static = new H3.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H3.KeyMgmntResponseMutableHeaderType { ReturnCode = "000000", ReportText = "OK" },
                },
                Body = new H3.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H3.EbicsKeyManagementResponseBodyReturnCode { Value = "000000" },
                    DataTransfer = new H3.EbicsKeyManagementResponseBodyDataTransfer
                    {
                        DataEncryptionInfo = new H3.EbicsKeyManagementResponseBodyDataTransferDataEncryptionInfo { TransactionKey = transactionKey },
                        OrderData = new H3.EbicsKeyManagementResponseBodyDataTransferOrderData { Value = orderData },
                    },
                },
            },
            version),
        EbicsVersion.H004 => EbicsXmlSerializer.SerializeToUtf8Bytes(
            new H4.EbicsKeyManagementResponse
            {
                Version = "H004",
                Header = new H4.EbicsKeyManagementResponseHeader
                {
                    Static = new H4.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H4.KeyMgmntResponseMutableHeaderType { ReturnCode = "000000", ReportText = "OK" },
                },
                Body = new H4.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H4.EbicsKeyManagementResponseBodyReturnCode { Value = "000000" },
                    DataTransfer = new H4.EbicsKeyManagementResponseBodyDataTransfer
                    {
                        DataEncryptionInfo = new H4.EbicsKeyManagementResponseBodyDataTransferDataEncryptionInfo { TransactionKey = transactionKey },
                        OrderData = new H4.EbicsKeyManagementResponseBodyDataTransferOrderData { Value = orderData },
                    },
                },
            },
            version),
        _ => EbicsXmlSerializer.SerializeToUtf8Bytes(
            new H5.EbicsKeyManagementResponse
            {
                Version = "H005",
                Header = new H5.EbicsKeyManagementResponseHeader
                {
                    Static = new H5.EbicsKeyManagementResponseHeaderStatic(),
                    Mutable = new H5.KeyMgmntResponseMutableHeaderType { ReturnCode = "000000", ReportText = "OK" },
                },
                Body = new H5.EbicsKeyManagementResponseBody
                {
                    ReturnCode = new H5.EbicsKeyManagementResponseBodyReturnCode { Value = "000000" },
                    DataTransfer = new H5.EbicsKeyManagementResponseBodyDataTransfer
                    {
                        DataEncryptionInfo = new H5.EbicsKeyManagementResponseBodyDataTransferDataEncryptionInfo { TransactionKey = transactionKey },
                        OrderData = new H5.EbicsKeyManagementResponseBodyDataTransferOrderData { Value = orderData },
                    },
                },
            },
            version),
    };

    /// <summary>Deserializes the version's unsecured request and returns its decompressed order data.</summary>
    public static byte[] DecompressedUnsecuredOrderData(EbicsVersion version, byte[] requestPayload)
    {
        var xml = Encoding.UTF8.GetString(requestPayload);
        var value = version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.Deserialize<H3.EbicsUnsecuredRequest>(xml).Body.DataTransfer.OrderData.Value,
            EbicsVersion.H004 => EbicsXmlSerializer.Deserialize<H4.EbicsUnsecuredRequest>(xml).Body.DataTransfer.OrderData.Value,
            _ => EbicsXmlSerializer.Deserialize<H5.EbicsUnsecuredRequest>(xml).Body.DataTransfer.OrderData.Value,
        };
        return EbicsCompression.Decompress(value);
    }

    /// <summary>Extracts the signature public key embedded in an INI order data payload.</summary>
    public static RsaKeyMaterial IniSignatureKey(EbicsVersion version, byte[] orderDataXml)
    {
        var xml = Encoding.UTF8.GetString(orderDataXml);
        if (version == EbicsVersion.H005)
        {
            var od = EbicsXmlSerializer.Deserialize<S2.SignaturePubKeyOrderDataType>(xml);
            return FromCertificate(od.SignaturePubKeyInfo.X509Data);
        }

        var legacy = EbicsXmlSerializer.Deserialize<S1.SignaturePubKeyOrderDataType>(xml);
        return FromRsaKeyValue(legacy.SignaturePubKeyInfo.PubKeyValue.RsaKeyValue);
    }

    /// <summary>Extracts the authentication and encryption public keys embedded in a HIA order data payload.</summary>
    public static (RsaKeyMaterial Authentication, RsaKeyMaterial Encryption) HiaKeys(EbicsVersion version, byte[] orderDataXml)
    {
        var xml = Encoding.UTF8.GetString(orderDataXml);
        switch (version)
        {
            case EbicsVersion.H005:
                var od5 = EbicsXmlSerializer.Deserialize<H5.HiaRequestOrderDataType>(xml);
                return (FromCertificate(od5.AuthenticationPubKeyInfo.X509Data), FromCertificate(od5.EncryptionPubKeyInfo.X509Data));
            case EbicsVersion.H004:
                var od4 = EbicsXmlSerializer.Deserialize<H4.HiaRequestOrderDataType>(xml);
                return (FromRsaKeyValue(od4.AuthenticationPubKeyInfo.PubKeyValue.RsaKeyValue), FromRsaKeyValue(od4.EncryptionPubKeyInfo.PubKeyValue.RsaKeyValue));
            default:
                var od3 = EbicsXmlSerializer.Deserialize<H3.HiaRequestOrderDataType>(xml);
                return (FromRsaKeyValue(od3.AuthenticationPubKeyInfo.PubKeyValue.RsaKeyValue), FromRsaKeyValue(od3.EncryptionPubKeyInfo.PubKeyValue.RsaKeyValue));
        }
    }

    private static RsaKeyMaterial FromCertificate(EBICO.Core.Schema.XmlDsig.X509DataType x509Data)
    {
        using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(x509Data.X509Certificate[0]);
        return RsaKeyImportExport.ImportPublicKeyFromCertificate(cert);
    }

    private static RsaKeyMaterial FromRsaKeyValue(EBICO.Core.Schema.XmlDsig.RsaKeyValueType rsaKeyValue)
        => RsaKeyImportExport.ImportRsaKeyValue(rsaKeyValue.Modulus, rsaKeyValue.Exponent);

    private static EBICO.Core.Schema.XmlDsig.X509DataType X509(byte[] der)
    {
        var data = new EBICO.Core.Schema.XmlDsig.X509DataType();
        data.X509Certificate.Add(der);
        return data;
    }

    private static EBICO.Core.Schema.XmlDsig.RsaKeyValueType RsaKeyValue(RsaKeyMaterial key)
    {
        var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(key);
        return new EBICO.Core.Schema.XmlDsig.RsaKeyValueType { Modulus = modulus, Exponent = exponent };
    }
}
