using System.Security.Cryptography.X509Certificates;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using Ds = EBICO.Core.Schema.XmlDsig;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;
using S001 = EBICO.Core.Schema.Signature.S001;
using S002 = EBICO.Core.Schema.Signature.S002;

namespace EBICO.Tests.Server;

/// <summary>
/// Shared helpers for the EBICO.Server tests (issues #25/#26/#27): builds well-formed request XML from
/// the committed Core bindings (no proprietary fixtures) and reads the return codes out of a response.
/// </summary>
internal static class ServerTestHelpers
{
    /// <summary>
    /// Builds a well-formed H004 <c>ebicsRequest</c> carrying <paramref name="orderType"/> in its
    /// static header, serialized to a string.
    /// </summary>
    /// <param name="orderType">The three-character order type (e.g. <c>"AAA"</c>).</param>
    /// <returns>The serialized request XML.</returns>
    public static string BuildH004Request(string orderType)
    {
        var request = new H004.EbicsRequest
        {
            Version = "H004",
            Header = new H004.EbicsRequestHeader
            {
                Static = new H004.StaticHeaderType
                {
                    HostId = "EBICOHOST",
                    PartnerId = "PARTNER01",
                    UserId = "USER01",
                    OrderDetails = new H004.StaticHeaderOrderDetailsType
                    {
                        OrderType = new H004.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                    },
                },
                Mutable = new H004.MutableHeaderType(),
            },
        };

        return EbicsXmlSerializer.SerializeToString(request, EbicsVersion.H004);
    }

    /// <summary>
    /// Builds a well-formed INI <c>ebicsUnsecuredRequest</c> for <paramref name="version"/> whose order
    /// data carries the given signature key: as an <c>RSAKeyValue</c> (H003/H004, <c>S001</c>) or as an
    /// <c>X509Data</c> certificate (H005, <c>S002</c>). Pass <paramref name="rsaKey"/> for H003/H004 and
    /// <paramref name="certificate"/> for H005.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header and order data.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header and order data.</param>
    /// <param name="signatureVersion">The <c>SignatureVersion</c> code (e.g. <c>"A005"</c>).</param>
    /// <param name="rsaKey">The signature key for H003/H004 (ignored for H005).</param>
    /// <param name="certificate">The signature certificate for H005 (ignored for H003/H004).</param>
    /// <returns>The serialized unsecured request XML.</returns>
    public static string BuildUnsecuredIniRequest(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        string signatureVersion = "A005",
        RsaKeyMaterial? rsaKey = null,
        X509Certificate2? certificate = null)
    {
        var orderData = version == EbicsVersion.H005
            ? SerializeS002OrderData(signatureVersion, partnerId, userId, certificate!)
            : SerializeS001OrderData(signatureVersion, partnerId, userId, rsaKey!);

        return BuildUnsecuredRequestWithOrderData(version, hostId, partnerId, userId, EbicsCompression.Compress(orderData), "INI");
    }

    /// <summary>
    /// Builds an <c>ebicsUnsecuredRequest</c> for <paramref name="version"/> and
    /// <paramref name="orderType"/> (<c>"INI"</c>/<c>"HIA"</c>) carrying <paramref name="compressedOrderData"/>
    /// verbatim as its <c>OrderData</c> (also used by negative tests with malformed/empty order data).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header.</param>
    /// <param name="compressedOrderData">The raw <c>OrderData</c> bytes (need not be valid).</param>
    /// <param name="orderType">The unsecured order type (<c>OrderType</c> for H003/H004, <c>AdminOrderType</c> for H005).</param>
    /// <returns>The serialized unsecured request XML.</returns>
    public static string BuildUnsecuredRequestWithOrderData(
        EbicsVersion version, string hostId, string partnerId, string userId, byte[] compressedOrderData, string orderType = "INI")
        => version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsUnsecuredRequest
            {
                Version = "H003",
                Header = new H003.EbicsUnsecuredRequestHeader
                {
                    Static = new H003.UnsecuredRequestStaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H003.UnsecuredReqOrderDetailsType { OrderType = orderType, OrderAttribute = "DZNNN" },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H003.EmptyMutableHeaderType(),
                },
                Body = new H003.EbicsUnsecuredRequestBody
                {
                    DataTransfer = new H003.EbicsUnsecuredRequestBodyDataTransfer
                    {
                        OrderData = new H003.EbicsUnsecuredRequestBodyDataTransferOrderData { Value = compressedOrderData },
                    },
                },
            }),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsUnsecuredRequest
            {
                Version = "H004",
                Header = new H004.EbicsUnsecuredRequestHeader
                {
                    Static = new H004.UnsecuredRequestStaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H004.UnsecuredReqOrderDetailsType { OrderType = orderType, OrderAttribute = "DZNNN" },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H004.EmptyMutableHeaderType(),
                },
                Body = new H004.EbicsUnsecuredRequestBody
                {
                    DataTransfer = new H004.EbicsUnsecuredRequestBodyDataTransfer
                    {
                        OrderData = new H004.EbicsUnsecuredRequestBodyDataTransferOrderData { Value = compressedOrderData },
                    },
                },
            }),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsUnsecuredRequest
            {
                Version = "H005",
                Header = new H005.EbicsUnsecuredRequestHeader
                {
                    Static = new H005.UnsecuredRequestStaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H005.UnsecuredReqOrderDetailsType { AdminOrderType = orderType },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H005.EmptyMutableHeaderType(),
                },
                Body = new H005.EbicsUnsecuredRequestBody
                {
                    DataTransfer = new H005.EbicsUnsecuredRequestBodyDataTransfer
                    {
                        OrderData = new H005.EbicsUnsecuredRequestBodyDataTransferOrderData { Value = compressedOrderData },
                    },
                },
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

    /// <summary>
    /// Builds a well-formed HIA <c>ebicsUnsecuredRequest</c> for <paramref name="version"/> whose order
    /// data carries the authentication (X00x) and encryption (E00x) keys: as <c>RSAKeyValue</c> elements
    /// (H003/H004) or as <c>X509Data</c> certificates (H005, one per key). Pass <paramref name="rsaAuthKey"/>/
    /// <paramref name="rsaEncKey"/> for H003/H004 and <paramref name="authCertificate"/>/
    /// <paramref name="encCertificate"/> for H005.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header and order data.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header and order data.</param>
    /// <param name="authenticationVersion">The <c>AuthenticationVersion</c> code (e.g. <c>"X002"</c>).</param>
    /// <param name="encryptionVersion">The <c>EncryptionVersion</c> code (e.g. <c>"E002"</c>).</param>
    /// <param name="rsaAuthKey">The authentication key for H003/H004 (ignored for H005).</param>
    /// <param name="rsaEncKey">The encryption key for H003/H004 (ignored for H005).</param>
    /// <param name="authCertificate">The authentication certificate for H005 (ignored for H003/H004).</param>
    /// <param name="encCertificate">The encryption certificate for H005 (ignored for H003/H004).</param>
    /// <returns>The serialized unsecured request XML.</returns>
    public static string BuildUnsecuredHiaRequest(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        string authenticationVersion = "X002",
        string encryptionVersion = "E002",
        RsaKeyMaterial? rsaAuthKey = null,
        RsaKeyMaterial? rsaEncKey = null,
        X509Certificate2? authCertificate = null,
        X509Certificate2? encCertificate = null)
    {
        var orderData = version switch
        {
            EbicsVersion.H003 => SerializeH003HiaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, rsaAuthKey!, rsaEncKey!),
            EbicsVersion.H004 => SerializeH004HiaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, rsaAuthKey!, rsaEncKey!),
            EbicsVersion.H005 => SerializeH005HiaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, authCertificate!, encCertificate!),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

        return BuildUnsecuredRequestWithOrderData(version, hostId, partnerId, userId, EbicsCompression.Compress(orderData), "HIA");
    }

    /// <summary>Reads the header (technical) and body (business) return codes of a response envelope.</summary>
    /// <param name="envelope">The response envelope (<c>ebicsResponse</c> or <c>ebicsKeyManagementResponse</c>).</param>
    /// <returns>The mutable-header return code and the body return code.</returns>
    public static (string? HeaderCode, string? BodyCode) ReadReturnCodes(IEbicsEnvelope envelope) => envelope switch
    {
        H003.EbicsResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        H004.EbicsResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        H005.EbicsResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        H003.EbicsKeyManagementResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        H004.EbicsKeyManagementResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        H005.EbicsKeyManagementResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        _ => (null, null),
    };

    private static byte[] SerializeS001OrderData(string signatureVersion, string partnerId, string userId, RsaKeyMaterial key)
    {
        var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(key);
        var orderData = new S001.SignaturePubKeyOrderDataType
        {
            SignaturePubKeyInfo = new S001.SignaturePubKeyInfoType
            {
                SignatureVersion = signatureVersion,
                PubKeyValue = new S001.PubKeyValueType
                {
                    RsaKeyValue = new Ds.RsaKeyValueType { Modulus = modulus, Exponent = exponent },
                },
            },
            PartnerId = partnerId,
            UserId = userId,
        };

        return EbicsXmlSerializer.SerializeOrderData(orderData);
    }

    private static byte[] SerializeS002OrderData(string signatureVersion, string partnerId, string userId, X509Certificate2 certificate)
    {
        var x509Data = new Ds.X509DataType();
        x509Data.X509Certificate.Add(certificate.RawData);

        var orderData = new S002.SignaturePubKeyOrderDataType
        {
            SignaturePubKeyInfo = new S002.SignaturePubKeyInfoType
            {
                SignatureVersion = signatureVersion,
                X509Data = x509Data,
            },
            PartnerId = partnerId,
            UserId = userId,
        };

        return EbicsXmlSerializer.SerializeOrderData(orderData);
    }

    private static byte[] SerializeH003HiaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
    {
        var (authModulus, authExponent) = RsaKeyImportExport.ExportRsaKeyValue(authKey);
        var (encModulus, encExponent) = RsaKeyImportExport.ExportRsaKeyValue(encKey);
        var orderData = new H003.HiaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H003.AuthenticationPubKeyInfoType
            {
                AuthenticationVersion = authVersion,
                PubKeyValue = new H003.PubKeyValueType
                {
                    RsaKeyValue = new Ds.RsaKeyValueType { Modulus = authModulus, Exponent = authExponent },
                },
            },
            EncryptionPubKeyInfo = new H003.EncryptionPubKeyInfoType
            {
                EncryptionVersion = encVersion,
                PubKeyValue = new H003.PubKeyValueType
                {
                    RsaKeyValue = new Ds.RsaKeyValueType { Modulus = encModulus, Exponent = encExponent },
                },
            },
            PartnerId = partnerId,
            UserId = userId,
        };

        return EbicsXmlSerializer.SerializeOrderData(orderData);
    }

    private static byte[] SerializeH004HiaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
    {
        var (authModulus, authExponent) = RsaKeyImportExport.ExportRsaKeyValue(authKey);
        var (encModulus, encExponent) = RsaKeyImportExport.ExportRsaKeyValue(encKey);
        var orderData = new H004.HiaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H004.AuthenticationPubKeyInfoType
            {
                AuthenticationVersion = authVersion,
                PubKeyValue = new H004.PubKeyValueType
                {
                    RsaKeyValue = new Ds.RsaKeyValueType { Modulus = authModulus, Exponent = authExponent },
                },
            },
            EncryptionPubKeyInfo = new H004.EncryptionPubKeyInfoType
            {
                EncryptionVersion = encVersion,
                PubKeyValue = new H004.PubKeyValueType
                {
                    RsaKeyValue = new Ds.RsaKeyValueType { Modulus = encModulus, Exponent = encExponent },
                },
            },
            PartnerId = partnerId,
            UserId = userId,
        };

        return EbicsXmlSerializer.SerializeOrderData(orderData);
    }

    private static byte[] SerializeH005HiaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, X509Certificate2 authCertificate, X509Certificate2 encCertificate)
    {
        var authX509 = new Ds.X509DataType();
        authX509.X509Certificate.Add(authCertificate.RawData);
        var encX509 = new Ds.X509DataType();
        encX509.X509Certificate.Add(encCertificate.RawData);

        var orderData = new H005.HiaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H005.AuthenticationPubKeyInfoType
            {
                AuthenticationVersion = authVersion,
                X509Data = authX509,
            },
            EncryptionPubKeyInfo = new H005.EncryptionPubKeyInfoType
            {
                EncryptionVersion = encVersion,
                X509Data = encX509,
            },
            PartnerId = partnerId,
            UserId = userId,
        };

        return EbicsXmlSerializer.SerializeOrderData(orderData);
    }
}
