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
/// Shared helpers for the EBICO.Server tests (issues #25/#26): builds well-formed request XML from the
/// committed Core bindings (no proprietary fixtures) and reads the return codes out of a response.
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

        return BuildUnsecuredIniRequestWithOrderData(version, hostId, partnerId, userId, EbicsCompression.Compress(orderData));
    }

    /// <summary>
    /// Builds an INI <c>ebicsUnsecuredRequest</c> for <paramref name="version"/> carrying
    /// <paramref name="compressedOrderData"/> verbatim as its <c>OrderData</c> (for negative tests with
    /// malformed/empty order data).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header.</param>
    /// <param name="compressedOrderData">The raw <c>OrderData</c> bytes (need not be valid).</param>
    /// <returns>The serialized unsecured request XML.</returns>
    public static string BuildUnsecuredIniRequestWithOrderData(
        EbicsVersion version, string hostId, string partnerId, string userId, byte[] compressedOrderData)
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
                        OrderDetails = new H003.UnsecuredReqOrderDetailsType { OrderType = "INI", OrderAttribute = "DZNNN" },
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
                        OrderDetails = new H004.UnsecuredReqOrderDetailsType { OrderType = "INI", OrderAttribute = "DZNNN" },
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
                        OrderDetails = new H005.UnsecuredReqOrderDetailsType { AdminOrderType = "INI" },
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
}
