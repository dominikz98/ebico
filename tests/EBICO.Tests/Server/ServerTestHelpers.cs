using System.Security.Cryptography.X509Certificates;
using EBICO.Core;
using EBICO.Core.Btf;
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
/// Shared helpers for the EBICO.Server tests (issues #25/#26/#27/#28): builds well-formed request XML
/// from the committed Core bindings (no proprietary fixtures) and reads the return codes out of a response.
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

    /// <summary>
    /// Builds a well-formed HPB <c>ebicsNoPubKeyDigestsRequest</c> for <paramref name="version"/> carrying
    /// the subscriber identifiers and order type in its static header (<c>OrderType</c> for H003/H004,
    /// <c>AdminOrderType</c> for H005). The <c>AuthSignature</c> is omitted — the server does not verify
    /// the request signature yet (M4).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header.</param>
    /// <param name="orderType">The order type (default <c>"HPB"</c>).</param>
    /// <returns>The serialized no-pub-key-digests request XML.</returns>
    public static string BuildNoPubKeyDigestsHpbRequest(
        EbicsVersion version, string hostId, string partnerId, string userId, string orderType = "HPB")
        => version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsNoPubKeyDigestsRequest
            {
                Version = "H003",
                Header = new H003.EbicsNoPubKeyDigestsRequestHeader
                {
                    Static = new H003.NoPubKeyDigestsRequestStaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H003.NoPubKeyDigestsReqOrderDetailsType { OrderType = orderType, OrderAttribute = "DZHNN" },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H003.EmptyMutableHeaderType(),
                },
                Body = new H003.EbicsNoPubKeyDigestsRequestBody(),
            }),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsNoPubKeyDigestsRequest
            {
                Version = "H004",
                Header = new H004.EbicsNoPubKeyDigestsRequestHeader
                {
                    Static = new H004.NoPubKeyDigestsRequestStaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H004.NoPubKeyDigestsReqOrderDetailsType { OrderType = orderType, OrderAttribute = "DZHNN" },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H004.EmptyMutableHeaderType(),
                },
                Body = new H004.EbicsNoPubKeyDigestsRequestBody(),
            }),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsNoPubKeyDigestsRequest
            {
                Version = "H005",
                Header = new H005.EbicsNoPubKeyDigestsRequestHeader
                {
                    Static = new H005.NoPubKeyDigestsRequestStaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H005.NoPubKeyDigestsReqOrderDetailsType { AdminOrderType = orderType },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H005.EmptyMutableHeaderType(),
                },
                Body = new H005.EbicsNoPubKeyDigestsRequestBody(),
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

    /// <summary>Reads the transaction id from the static header of an <c>ebicsResponse</c>, or <see langword="null"/>.</summary>
    /// <param name="envelope">The response envelope.</param>
    /// <returns>The 16-byte transaction id, or <see langword="null"/> when absent.</returns>
    public static byte[]? ReadTransactionId(IEbicsEnvelope envelope) => envelope switch
    {
        H003.EbicsResponse r => r.Header?.Static?.TransactionId,
        H004.EbicsResponse r => r.Header?.Static?.TransactionId,
        H005.EbicsResponse r => r.Header?.Static?.TransactionId,
        _ => null,
    };

    /// <summary>Reads the transaction phase (as its enum name) from the mutable header of an <c>ebicsResponse</c>.</summary>
    /// <param name="envelope">The response envelope.</param>
    /// <returns>The phase name (<c>"Initialisation"</c>/<c>"Transfer"</c>/<c>"Receipt"</c>), or <see langword="null"/>.</returns>
    public static string? ReadTransactionPhase(IEbicsEnvelope envelope) => envelope switch
    {
        H003.EbicsResponse r => r.Header?.Mutable?.TransactionPhase.ToString(),
        H004.EbicsResponse r => r.Header?.Mutable?.TransactionPhase.ToString(),
        H005.EbicsResponse r => r.Header?.Mutable?.TransactionPhase.ToString(),
        _ => null,
    };

    /// <summary>Reads <c>NumSegments</c> from the static header of an <c>ebicsResponse</c> (download initialisation), or <see langword="null"/>.</summary>
    /// <param name="envelope">The response envelope.</param>
    /// <returns>The announced segment count, or <see langword="null"/> when absent.</returns>
    public static ulong? ReadNumSegments(IEbicsEnvelope envelope) => envelope switch
    {
        H003.EbicsResponse r => r.Header?.Static?.NumSegments,
        H004.EbicsResponse r => r.Header?.Static?.NumSegments,
        H005.EbicsResponse r => r.Header?.Static?.NumSegments,
        _ => null,
    };

    /// <summary>Reads the acknowledged segment number (and its last-segment flag) from the mutable header of an <c>ebicsResponse</c>.</summary>
    /// <param name="envelope">The response envelope.</param>
    /// <returns>The segment number (or <see langword="null"/>) and whether it is the last segment.</returns>
    public static (ulong? Value, bool LastSegment) ReadSegmentNumber(IEbicsEnvelope envelope) => envelope switch
    {
        H003.EbicsResponse r => (r.Header?.Mutable?.SegmentNumber?.Value, r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false),
        H004.EbicsResponse r => (r.Header?.Mutable?.SegmentNumber?.Value, r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false),
        H005.EbicsResponse r => (r.Header?.Mutable?.SegmentNumber?.Value, r.Header?.Mutable?.SegmentNumber?.LastSegment ?? false),
        _ => (null, false),
    };

    /// <summary>
    /// Reads the download <c>body/DataTransfer</c> of an <c>ebicsResponse</c>: the (base64-decoded)
    /// order-data segment and, on the initialisation segment, the encrypted transaction key and the
    /// recipient-key digest (both <see langword="null"/> on transfer segments).
    /// </summary>
    /// <param name="version">The protocol version whose bindings to read.</param>
    /// <param name="envelope">The response envelope.</param>
    /// <returns>The encrypted transaction key, the order-data segment, the digest version and the digest.</returns>
    public static (byte[]? TransactionKey, byte[]? OrderData, string? DigestVersion, byte[]? Digest) ReadDownloadDataTransfer(
        EbicsVersion version, IEbicsEnvelope envelope)
    {
        switch (version)
        {
            case EbicsVersion.H003:
                var t3 = ((H003.EbicsResponse)envelope).Body?.DataTransfer;
                return (t3?.DataEncryptionInfo?.TransactionKey, t3?.OrderData?.Value, t3?.DataEncryptionInfo?.EncryptionPubKeyDigest?.Version, t3?.DataEncryptionInfo?.EncryptionPubKeyDigest?.Value);
            case EbicsVersion.H004:
                var t4 = ((H004.EbicsResponse)envelope).Body?.DataTransfer;
                return (t4?.DataEncryptionInfo?.TransactionKey, t4?.OrderData?.Value, t4?.DataEncryptionInfo?.EncryptionPubKeyDigest?.Version, t4?.DataEncryptionInfo?.EncryptionPubKeyDigest?.Value);
            default:
                var t5 = ((H005.EbicsResponse)envelope).Body?.DataTransfer;
                return (t5?.DataEncryptionInfo?.TransactionKey, t5?.OrderData?.Value, t5?.DataEncryptionInfo?.EncryptionPubKeyDigest?.Version, t5?.DataEncryptionInfo?.EncryptionPubKeyDigest?.Value);
        }
    }

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

    // --- Issue #29: HSA (unsecured) / HCA / HCS (encrypted ebicsRequest) / SPR --------------

    /// <summary>
    /// Builds a well-formed HSA <c>ebicsUnsecuredRequest</c> (H003/H004 only — HSA does not exist in
    /// H005) whose order data carries the authentication (X00x) and encryption (E00x) keys as
    /// <c>RSAKeyValue</c> elements.
    /// </summary>
    /// <param name="version">The protocol version (H003 or H004).</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header and order data.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header and order data.</param>
    /// <param name="authenticationVersion">The <c>AuthenticationVersion</c> code (e.g. <c>"X002"</c>).</param>
    /// <param name="encryptionVersion">The <c>EncryptionVersion</c> code (e.g. <c>"E002"</c>).</param>
    /// <param name="rsaAuthKey">The authentication key.</param>
    /// <param name="rsaEncKey">The encryption key.</param>
    /// <returns>The serialized unsecured request XML.</returns>
    public static string BuildUnsecuredHsaRequest(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        string authenticationVersion = "X002",
        string encryptionVersion = "E002",
        RsaKeyMaterial? rsaAuthKey = null,
        RsaKeyMaterial? rsaEncKey = null)
    {
        var orderData = version switch
        {
            EbicsVersion.H003 => SerializeH003HsaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, rsaAuthKey!, rsaEncKey!),
            EbicsVersion.H004 => SerializeH004HsaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, rsaAuthKey!, rsaEncKey!),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "HSA exists only for H003/H004."),
        };

        return BuildUnsecuredRequestWithOrderData(version, hostId, partnerId, userId, EbicsCompression.Compress(orderData), "HSA");
    }

    /// <summary>
    /// Builds a well-formed HCA <c>ebicsRequest</c> whose order data (new authentication + encryption
    /// keys) is compressed and E002-encrypted for the bank's public encryption key
    /// (<paramref name="bankEncKey"/>/<paramref name="bankEncVersion"/>). Pass RSA keys for H003/H004 and
    /// certificates for H005.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header and order data.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header and order data.</param>
    /// <param name="bankEncKey">The bank's public encryption key the order data is encrypted for.</param>
    /// <param name="bankEncVersion">The bank's encryption key version (e.g. <c>"E002"</c>).</param>
    /// <param name="authenticationVersion">The new <c>AuthenticationVersion</c> code.</param>
    /// <param name="encryptionVersion">The new <c>EncryptionVersion</c> code.</param>
    /// <param name="rsaAuthKey">The new authentication key for H003/H004 (ignored for H005).</param>
    /// <param name="rsaEncKey">The new encryption key for H003/H004 (ignored for H005).</param>
    /// <param name="authCertificate">The new authentication certificate for H005 (ignored for H003/H004).</param>
    /// <param name="encCertificate">The new encryption certificate for H005 (ignored for H003/H004).</param>
    /// <returns>The serialized request XML.</returns>
    public static string BuildEncryptedHcaRequest(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        RsaKeyMaterial bankEncKey,
        KeyVersion bankEncVersion,
        string authenticationVersion = "X002",
        string encryptionVersion = "E002",
        RsaKeyMaterial? rsaAuthKey = null,
        RsaKeyMaterial? rsaEncKey = null,
        X509Certificate2? authCertificate = null,
        X509Certificate2? encCertificate = null)
    {
        var orderData = version switch
        {
            EbicsVersion.H003 => SerializeH003HcaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, rsaAuthKey!, rsaEncKey!),
            EbicsVersion.H004 => SerializeH004HcaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, rsaAuthKey!, rsaEncKey!),
            EbicsVersion.H005 => SerializeH005HcaOrderData(authenticationVersion, encryptionVersion, partnerId, userId, authCertificate!, encCertificate!),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

        return BuildEncryptedRequestWithOrderData(version, hostId, partnerId, userId, EbicsCompression.Compress(orderData), "HCA", bankEncKey, bankEncVersion);
    }

    /// <summary>
    /// Builds a well-formed HCS <c>ebicsRequest</c> whose order data (new signature + authentication +
    /// encryption keys) is compressed and E002-encrypted for the bank's public encryption key. Pass RSA
    /// keys for H003/H004 and certificates for H005.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header and order data.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header and order data.</param>
    /// <param name="bankEncKey">The bank's public encryption key the order data is encrypted for.</param>
    /// <param name="bankEncVersion">The bank's encryption key version (e.g. <c>"E002"</c>).</param>
    /// <param name="signatureVersion">The new <c>SignatureVersion</c> code (e.g. <c>"A005"</c>).</param>
    /// <param name="authenticationVersion">The new <c>AuthenticationVersion</c> code.</param>
    /// <param name="encryptionVersion">The new <c>EncryptionVersion</c> code.</param>
    /// <param name="rsaSigKey">The new signature key for H003/H004 (ignored for H005).</param>
    /// <param name="rsaAuthKey">The new authentication key for H003/H004 (ignored for H005).</param>
    /// <param name="rsaEncKey">The new encryption key for H003/H004 (ignored for H005).</param>
    /// <param name="sigCertificate">The new signature certificate for H005 (ignored for H003/H004).</param>
    /// <param name="authCertificate">The new authentication certificate for H005 (ignored for H003/H004).</param>
    /// <param name="encCertificate">The new encryption certificate for H005 (ignored for H003/H004).</param>
    /// <returns>The serialized request XML.</returns>
    public static string BuildEncryptedHcsRequest(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        RsaKeyMaterial bankEncKey,
        KeyVersion bankEncVersion,
        string signatureVersion = "A005",
        string authenticationVersion = "X002",
        string encryptionVersion = "E002",
        RsaKeyMaterial? rsaSigKey = null,
        RsaKeyMaterial? rsaAuthKey = null,
        RsaKeyMaterial? rsaEncKey = null,
        X509Certificate2? sigCertificate = null,
        X509Certificate2? authCertificate = null,
        X509Certificate2? encCertificate = null)
    {
        var orderData = version switch
        {
            EbicsVersion.H003 => SerializeH003HcsOrderData(signatureVersion, authenticationVersion, encryptionVersion, partnerId, userId, rsaSigKey!, rsaAuthKey!, rsaEncKey!),
            EbicsVersion.H004 => SerializeH004HcsOrderData(signatureVersion, authenticationVersion, encryptionVersion, partnerId, userId, rsaSigKey!, rsaAuthKey!, rsaEncKey!),
            EbicsVersion.H005 => SerializeH005HcsOrderData(signatureVersion, authenticationVersion, encryptionVersion, partnerId, userId, sigCertificate!, authCertificate!, encCertificate!),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

        return BuildEncryptedRequestWithOrderData(version, hostId, partnerId, userId, EbicsCompression.Compress(orderData), "HCS", bankEncKey, bankEncVersion);
    }

    /// <summary>
    /// Builds an <c>ebicsRequest</c> for <paramref name="version"/>/<paramref name="orderType"/> carrying
    /// <paramref name="compressedOrderData"/> E002-encrypted for the bank's public encryption key
    /// (<paramref name="bankEncKey"/>). Also used by negative tests with undecompressable payloads.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header.</param>
    /// <param name="compressedOrderData">The order-data bytes to encrypt (need not be valid).</param>
    /// <param name="orderType">The order type (<c>OrderType</c> for H003/H004, <c>AdminOrderType</c> for H005).</param>
    /// <param name="bankEncKey">The bank's public encryption key the order data is encrypted for.</param>
    /// <param name="bankEncVersion">The bank's encryption key version.</param>
    /// <returns>The serialized request XML.</returns>
    public static string BuildEncryptedRequestWithOrderData(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        byte[] compressedOrderData,
        string orderType,
        RsaKeyMaterial bankEncKey,
        KeyVersion bankEncVersion)
    {
        var encrypted = EncryptionE002.Encrypt(compressedOrderData, bankEncKey, bankEncVersion);
        var digest = PublicKeyFingerprint.Compute(bankEncKey);

        return version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsRequest
            {
                Version = "H003",
                Header = new H003.EbicsRequestHeader
                {
                    Static = new H003.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H003.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H003.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H003.MutableHeaderType(),
                },
                Body = new H003.EbicsRequestBody
                {
                    DataTransfer = new H003.DataTransferRequestType
                    {
                        DataEncryptionInfo = new H003.DataTransferRequestTypeDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H003.DataEncryptionInfoTypeEncryptionPubKeyDigest
                            {
                                Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                                Version = bankEncVersion.Value,
                                Value = digest,
                            },
                            TransactionKey = encrypted.EncryptedTransactionKey,
                        },
                        OrderData = new H003.DataTransferRequestTypeOrderData { Value = encrypted.EncryptedOrderDataBytes },
                    },
                },
            }, EbicsVersion.H003),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsRequest
            {
                Version = "H004",
                Header = new H004.EbicsRequestHeader
                {
                    Static = new H004.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H004.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H004.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H004.MutableHeaderType(),
                },
                Body = new H004.EbicsRequestBody
                {
                    DataTransfer = new H004.DataTransferRequestType
                    {
                        DataEncryptionInfo = new H004.DataTransferRequestTypeDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H004.DataEncryptionInfoTypeEncryptionPubKeyDigest
                            {
                                Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                                Version = bankEncVersion.Value,
                                Value = digest,
                            },
                            TransactionKey = encrypted.EncryptedTransactionKey,
                        },
                        OrderData = new H004.DataTransferRequestTypeOrderData { Value = encrypted.EncryptedOrderDataBytes },
                    },
                },
            }, EbicsVersion.H004),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsRequest
            {
                Version = "H005",
                Header = new H005.EbicsRequestHeader
                {
                    Static = new H005.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H005.StaticHeaderOrderDetailsType
                        {
                            AdminOrderType = new H005.StaticHeaderOrderDetailsTypeAdminOrderType { Value = orderType },
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H005.MutableHeaderType(),
                },
                Body = new H005.EbicsRequestBody
                {
                    DataTransfer = new H005.DataTransferRequestType
                    {
                        DataEncryptionInfo = new H005.DataTransferRequestTypeDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H005.DataEncryptionInfoTypeEncryptionPubKeyDigest
                            {
                                Algorithm = PublicKeyFingerprint.DigestAlgorithm,
                                Version = bankEncVersion.Value,
                                Value = digest,
                            },
                            TransactionKey = encrypted.EncryptedTransactionKey,
                        },
                        OrderData = new H005.DataTransferRequestTypeOrderData { Value = encrypted.EncryptedOrderDataBytes },
                    },
                },
            }, EbicsVersion.H005),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }

    /// <summary>
    /// Builds an <c>ebicsRequest</c> carrying order type SPR (suspension) and <b>no</b> order data
    /// (<c>OrderType</c> for H003/H004, <c>AdminOrderType</c> for H005).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header.</param>
    /// <param name="orderType">The order type (default <c>"SPR"</c>).</param>
    /// <returns>The serialized request XML.</returns>
    public static string BuildSprRequest(
        EbicsVersion version, string hostId, string partnerId, string userId, string orderType = "SPR")
        => version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsRequest
            {
                Version = "H003",
                Header = new H003.EbicsRequestHeader
                {
                    Static = new H003.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H003.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H003.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H003.MutableHeaderType(),
                },
            }, EbicsVersion.H003),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsRequest
            {
                Version = "H004",
                Header = new H004.EbicsRequestHeader
                {
                    Static = new H004.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H004.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H004.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H004.MutableHeaderType(),
                },
            }, EbicsVersion.H004),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsRequest
            {
                Version = "H005",
                Header = new H005.EbicsRequestHeader
                {
                    Static = new H005.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H005.StaticHeaderOrderDetailsType
                        {
                            AdminOrderType = new H005.StaticHeaderOrderDetailsTypeAdminOrderType { Value = orderType },
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H005.MutableHeaderType(),
                },
            }, EbicsVersion.H005),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

    // --- Issue #32: upload transaction (Initialisation + Transfer) --------------------------

    /// <summary>
    /// The artefacts of an upload transaction the tests drive: the initialisation-phase request XML and
    /// the ordered order-data segments to be delivered one per transfer-phase request.
    /// </summary>
    /// <param name="InitXml">The serialized initialisation-phase <c>ebicsRequest</c>.</param>
    /// <param name="Segments">The ordered AES-ciphertext segments (one <c>OrderData</c> per transfer).</param>
    public sealed record UploadRequest(string InitXml, IReadOnlyList<byte[]> Segments);

    /// <summary>
    /// Builds the initialisation-phase request for a generic upload (FUL for H003/H004, BTU for H005):
    /// <paramref name="orderData"/> is compressed, E002-encrypted for the bank's public encryption key and
    /// the ciphertext split into segments of at most <paramref name="segmentSizeBytes"/> bytes. The
    /// initialisation carries the encrypted transaction key and <c>NumSegments</c> (no <c>OrderData</c>);
    /// the returned segments feed <see cref="BuildUploadTransferRequest"/>.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header.</param>
    /// <param name="orderData">The plaintext order data to upload.</param>
    /// <param name="bankEncKey">The bank's public encryption key the transaction key is encrypted for.</param>
    /// <param name="bankEncVersion">The bank's encryption key version (e.g. <c>"E002"</c>).</param>
    /// <param name="segmentSizeBytes">The maximum raw segment size; small values force several segments.</param>
    /// <param name="signatureData">The optional raw order-signature (ES) blob to place in the initialisation.</param>
    /// <param name="btf">The optional H005 business transaction format placed in <c>BTUOrderParams/Service</c> (ignored for H003/H004).</param>
    /// <param name="orderType">The optional order type / admin order type to place in the header; defaults to <c>"FUL"</c> (H003/H004) / <c>"BTU"</c> (H005). Use this to submit a classical payment order type (e.g. <c>"CCT"</c>) directly.</param>
    /// <param name="fileFormat">The optional H003/H004 <c>FULOrderParams/FileFormat</c> value (e.g. <c>"pain.001.001.09"</c>); ignored for H005.</param>
    /// <returns>The initialisation XML and the ordered ciphertext segments.</returns>
    public static UploadRequest BuildUploadInitRequest(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        byte[] orderData,
        RsaKeyMaterial bankEncKey,
        KeyVersion bankEncVersion,
        int segmentSizeBytes = 512 * 1024,
        byte[]? signatureData = null,
        BusinessTransactionFormat? btf = null,
        string? orderType = null,
        string? fileFormat = null)
    {
        var compressed = EbicsCompression.Compress(orderData);
        var encrypted = EncryptionE002.Encrypt(compressed, bankEncKey, bankEncVersion);
        var segments = EbicsSegmentation.Split(encrypted.EncryptedOrderDataBytes, segmentSizeBytes).Segments;
        var numSegments = (ulong)segments.Count;
        orderType ??= version == EbicsVersion.H005 ? "BTU" : "FUL";
        var digest = PublicKeyFingerprint.Compute(bankEncKey);

        var xml = version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsRequest
            {
                Version = "H003",
                Header = new H003.EbicsRequestHeader
                {
                    Static = new H003.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H003.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H003.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                            OrderParams = fileFormat is null ? null : new H003.FulOrderParamsType { FileFormat = new H003.FileFormatType { Value = fileFormat } },
                        },
                        SecurityMedium = "0000",
                        NumSegments = numSegments,
                    },
                    Mutable = new H003.MutableHeaderType { TransactionPhase = H003.TransactionPhaseType.Initialisation },
                },
                Body = new H003.EbicsRequestBody
                {
                    DataTransfer = new H003.DataTransferRequestType
                    {
                        DataEncryptionInfo = new H003.DataTransferRequestTypeDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H003.DataEncryptionInfoTypeEncryptionPubKeyDigest { Algorithm = PublicKeyFingerprint.DigestAlgorithm, Version = bankEncVersion.Value, Value = digest },
                            TransactionKey = encrypted.EncryptedTransactionKey,
                        },
                        SignatureData = signatureData is null ? null : new H003.DataTransferRequestTypeSignatureData { Value = signatureData },
                    },
                },
            }, EbicsVersion.H003),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsRequest
            {
                Version = "H004",
                Header = new H004.EbicsRequestHeader
                {
                    Static = new H004.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H004.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H004.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                            OrderParams = fileFormat is null ? null : new H004.FulOrderParamsType { FileFormat = new H004.FileFormatType { Value = fileFormat } },
                        },
                        SecurityMedium = "0000",
                        NumSegments = numSegments,
                    },
                    Mutable = new H004.MutableHeaderType { TransactionPhase = H004.TransactionPhaseType.Initialisation },
                },
                Body = new H004.EbicsRequestBody
                {
                    DataTransfer = new H004.DataTransferRequestType
                    {
                        DataEncryptionInfo = new H004.DataTransferRequestTypeDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H004.DataEncryptionInfoTypeEncryptionPubKeyDigest { Algorithm = PublicKeyFingerprint.DigestAlgorithm, Version = bankEncVersion.Value, Value = digest },
                            TransactionKey = encrypted.EncryptedTransactionKey,
                        },
                        SignatureData = signatureData is null ? null : new H004.DataTransferRequestTypeSignatureData { Value = signatureData },
                    },
                },
            }, EbicsVersion.H004),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsRequest
            {
                Version = "H005",
                Header = new H005.EbicsRequestHeader
                {
                    Static = new H005.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H005.StaticHeaderOrderDetailsType
                        {
                            AdminOrderType = new H005.StaticHeaderOrderDetailsTypeAdminOrderType { Value = orderType },
                            OrderParams = btf is { } bu ? new H005.BtuParamsType { Service = bu.ToRestrictedServiceType() } : null,
                        },
                        SecurityMedium = "0000",
                        NumSegments = numSegments,
                    },
                    Mutable = new H005.MutableHeaderType { TransactionPhase = H005.TransactionPhaseType.Initialisation },
                },
                Body = new H005.EbicsRequestBody
                {
                    DataTransfer = new H005.DataTransferRequestType
                    {
                        DataEncryptionInfo = new H005.DataTransferRequestTypeDataEncryptionInfo
                        {
                            EncryptionPubKeyDigest = new H005.DataEncryptionInfoTypeEncryptionPubKeyDigest { Algorithm = PublicKeyFingerprint.DigestAlgorithm, Version = bankEncVersion.Value, Value = digest },
                            TransactionKey = encrypted.EncryptedTransactionKey,
                        },
                        SignatureData = signatureData is null ? null : new H005.DataTransferRequestTypeSignatureData { Value = signatureData },
                    },
                },
            }, EbicsVersion.H005),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

        return new UploadRequest(xml, segments);
    }

    /// <summary>
    /// Builds a transfer-phase <c>ebicsRequest</c> carrying one order-data <paramref name="segment"/>: the
    /// static header holds only the <paramref name="transactionId"/>, the mutable header the
    /// <c>Transfer</c> phase and the <paramref name="segmentNumber"/> (+ <paramref name="lastSegment"/>).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="transactionId">The transaction id assigned by the server in the initialisation response.</param>
    /// <param name="segmentNumber">The 1-based segment number.</param>
    /// <param name="lastSegment">Whether this is the last segment of the transaction.</param>
    /// <param name="segment">The raw order-data segment bytes.</param>
    /// <returns>The serialized transfer request XML.</returns>
    public static string BuildUploadTransferRequest(
        EbicsVersion version,
        string hostId,
        byte[] transactionId,
        ulong segmentNumber,
        bool lastSegment,
        byte[] segment)
        => version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsRequest
            {
                Version = "H003",
                Header = new H003.EbicsRequestHeader
                {
                    Static = new H003.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H003.MutableHeaderType
                    {
                        TransactionPhase = H003.TransactionPhaseType.Transfer,
                        SegmentNumber = new H003.MutableHeaderTypeSegmentNumber { Value = segmentNumber, LastSegment = lastSegment },
                    },
                },
                Body = new H003.EbicsRequestBody
                {
                    DataTransfer = new H003.DataTransferRequestType { OrderData = new H003.DataTransferRequestTypeOrderData { Value = segment } },
                },
            }, EbicsVersion.H003),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsRequest
            {
                Version = "H004",
                Header = new H004.EbicsRequestHeader
                {
                    Static = new H004.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H004.MutableHeaderType
                    {
                        TransactionPhase = H004.TransactionPhaseType.Transfer,
                        SegmentNumber = new H004.MutableHeaderTypeSegmentNumber { Value = segmentNumber, LastSegment = lastSegment },
                    },
                },
                Body = new H004.EbicsRequestBody
                {
                    DataTransfer = new H004.DataTransferRequestType { OrderData = new H004.DataTransferRequestTypeOrderData { Value = segment } },
                },
            }, EbicsVersion.H004),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsRequest
            {
                Version = "H005",
                Header = new H005.EbicsRequestHeader
                {
                    Static = new H005.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H005.MutableHeaderType
                    {
                        TransactionPhase = H005.TransactionPhaseType.Transfer,
                        SegmentNumber = new H005.MutableHeaderTypeSegmentNumber { Value = segmentNumber, LastSegment = lastSegment },
                    },
                },
                Body = new H005.EbicsRequestBody
                {
                    DataTransfer = new H005.DataTransferRequestType { OrderData = new H005.DataTransferRequestTypeOrderData { Value = segment } },
                },
            }, EbicsVersion.H005),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

    // --- Issue #33: download transaction (Initialisation + Transfer + Receipt) --------------

    /// <summary>
    /// Builds the initialisation-phase request for a generic download (FDL for H003/H004, BTD for H005):
    /// a signed <c>ebicsRequest</c> carrying the subscriber identifiers, the download order type and
    /// <c>TransactionPhase=Initialisation</c>. No body and no <c>NumSegments</c> (the client does not
    /// upload anything).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="partnerId">The <c>PartnerID</c> to place in the header.</param>
    /// <param name="userId">The <c>UserID</c> to place in the header.</param>
    /// <param name="btf">The optional H005 business transaction format placed in <c>BTDOrderParams/Service</c> (ignored for H003/H004).</param>
    /// <param name="orderType">The optional order/admin-order type to place in the header; defaults to <c>"FDL"</c> (H003/H004) / <c>"BTD"</c> (H005). Use this to request a classical download order type (e.g. <c>"STA"</c>) directly.</param>
    /// <param name="fileFormat">The optional H003/H004 <c>FDLOrderParams/FileFormat</c> value (e.g. <c>"camt.053"</c>, <c>"mt940"</c>); ignored for H005.</param>
    /// <param name="dateRange">The optional reporting period placed in the order params (all versions); both bounds must be set.</param>
    /// <returns>The serialized initialisation request XML.</returns>
    public static string BuildDownloadInitRequest(
        EbicsVersion version,
        string hostId,
        string partnerId,
        string userId,
        BusinessTransactionFormat? btf = null,
        string? orderType = null,
        string? fileFormat = null,
        DateRange? dateRange = null)
    {
        orderType ??= version == EbicsVersion.H005 ? "BTD" : "FDL";
        return version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsRequest
            {
                Version = "H003",
                Header = new H003.EbicsRequestHeader
                {
                    Static = new H003.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H003.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H003.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                            OrderParams = BuildH003DownloadParams(fileFormat, dateRange),
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H003.MutableHeaderType { TransactionPhase = H003.TransactionPhaseType.Initialisation },
                },
            }, EbicsVersion.H003),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsRequest
            {
                Version = "H004",
                Header = new H004.EbicsRequestHeader
                {
                    Static = new H004.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H004.StaticHeaderOrderDetailsType
                        {
                            OrderType = new H004.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                            OrderParams = BuildH004DownloadParams(fileFormat, dateRange),
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H004.MutableHeaderType { TransactionPhase = H004.TransactionPhaseType.Initialisation },
                },
            }, EbicsVersion.H004),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsRequest
            {
                Version = "H005",
                Header = new H005.EbicsRequestHeader
                {
                    Static = new H005.StaticHeaderType
                    {
                        HostId = hostId,
                        PartnerId = partnerId,
                        UserId = userId,
                        OrderDetails = new H005.StaticHeaderOrderDetailsType
                        {
                            AdminOrderType = new H005.StaticHeaderOrderDetailsTypeAdminOrderType { Value = orderType },
                            OrderParams = BuildH005DownloadParams(btf, dateRange),
                        },
                        SecurityMedium = "0000",
                    },
                    Mutable = new H005.MutableHeaderType { TransactionPhase = H005.TransactionPhaseType.Initialisation },
                },
            }, EbicsVersion.H005),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };
    }

    private static DateTime ToXsdDate(DateOnly? value) => value!.Value.ToDateTime(TimeOnly.MinValue);

    // H003 download order params: FDLOrderParams (FileFormat + optional DateRange) when a file format is
    // given, else StandardOrderParams (DateRange only) when a range is given, else none (the bare form).
    private static object? BuildH003DownloadParams(string? fileFormat, DateRange? dateRange)
    {
        if (fileFormat is not null)
        {
            return new H003.FdlOrderParamsType
            {
                FileFormat = new H003.FileFormatType { Value = fileFormat },
                DateRange = dateRange is { } dr ? new H003.FdlOrderParamsTypeDateRange { Start = ToXsdDate(dr.Start), End = ToXsdDate(dr.End) } : null,
            };
        }

        return dateRange is { } d
            ? new H003.StandardOrderParamsType { DateRange = new H003.StandardOrderParamsTypeDateRange { Start = ToXsdDate(d.Start), End = ToXsdDate(d.End) } }
            : null;
    }

    private static object? BuildH004DownloadParams(string? fileFormat, DateRange? dateRange)
    {
        if (fileFormat is not null)
        {
            return new H004.FdlOrderParamsType
            {
                FileFormat = new H004.FileFormatType { Value = fileFormat },
                DateRange = dateRange is { } dr ? new H004.FdlOrderParamsTypeDateRange { Start = ToXsdDate(dr.Start), End = ToXsdDate(dr.End) } : null,
            };
        }

        return dateRange is { } d
            ? new H004.StandardOrderParamsType { DateRange = new H004.StandardOrderParamsTypeDateRange { Start = ToXsdDate(d.Start), End = ToXsdDate(d.End) } }
            : null;
    }

    // H005 download order params: BTDOrderParams carrying the BTF service and/or the reporting period.
    // Kept null when neither is present, preserving the exact wire form for bare no-arg callers.
    private static H005.BtdParamsType? BuildH005DownloadParams(BusinessTransactionFormat? btf, DateRange? dateRange)
    {
        if (btf is null && dateRange is null)
        {
            return null;
        }

        return new H005.BtdParamsType
        {
            Service = btf is { } b ? b.ToRestrictedServiceType() : null,
            DateRange = dateRange is { } dr ? new H005.DateRangeType { Start = ToXsdDate(dr.Start), End = ToXsdDate(dr.End) } : null,
        };
    }

    /// <summary>
    /// Builds a transfer-phase <c>ebicsRequest</c> that fetches one download segment: the static header
    /// holds only the <paramref name="transactionId"/>, the mutable header the <c>Transfer</c> phase and
    /// the <paramref name="segmentNumber"/> (+ <paramref name="lastSegment"/>). No body.
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="transactionId">The transaction id assigned by the server in the initialisation response.</param>
    /// <param name="segmentNumber">The 1-based segment number to fetch.</param>
    /// <param name="lastSegment">Whether the client marks this as the last segment.</param>
    /// <returns>The serialized transfer request XML.</returns>
    public static string BuildDownloadTransferRequest(
        EbicsVersion version, string hostId, byte[] transactionId, ulong segmentNumber, bool lastSegment)
        => version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsRequest
            {
                Version = "H003",
                Header = new H003.EbicsRequestHeader
                {
                    Static = new H003.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H003.MutableHeaderType
                    {
                        TransactionPhase = H003.TransactionPhaseType.Transfer,
                        SegmentNumber = new H003.MutableHeaderTypeSegmentNumber { Value = segmentNumber, LastSegment = lastSegment },
                    },
                },
            }, EbicsVersion.H003),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsRequest
            {
                Version = "H004",
                Header = new H004.EbicsRequestHeader
                {
                    Static = new H004.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H004.MutableHeaderType
                    {
                        TransactionPhase = H004.TransactionPhaseType.Transfer,
                        SegmentNumber = new H004.MutableHeaderTypeSegmentNumber { Value = segmentNumber, LastSegment = lastSegment },
                    },
                },
            }, EbicsVersion.H004),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsRequest
            {
                Version = "H005",
                Header = new H005.EbicsRequestHeader
                {
                    Static = new H005.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H005.MutableHeaderType
                    {
                        TransactionPhase = H005.TransactionPhaseType.Transfer,
                        SegmentNumber = new H005.MutableHeaderTypeSegmentNumber { Value = segmentNumber, LastSegment = lastSegment },
                    },
                },
            }, EbicsVersion.H005),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

    /// <summary>
    /// Builds a receipt-phase <c>ebicsRequest</c> acknowledging a completed download: the static header
    /// holds the <paramref name="transactionId"/>, the mutable header the <c>Receipt</c> phase and the
    /// body a <c>TransferReceipt</c> with <paramref name="receiptCode"/> (0 = positive, 1 = negative).
    /// </summary>
    /// <param name="version">The protocol version.</param>
    /// <param name="hostId">The <c>HostID</c> to place in the header.</param>
    /// <param name="transactionId">The transaction id assigned by the server in the initialisation response.</param>
    /// <param name="receiptCode">The receipt code (0 = positive, non-0 = negative).</param>
    /// <returns>The serialized receipt request XML.</returns>
    public static string BuildDownloadReceiptRequest(
        EbicsVersion version, string hostId, byte[] transactionId, byte receiptCode)
        => version switch
        {
            EbicsVersion.H003 => EbicsXmlSerializer.SerializeToString(new H003.EbicsRequest
            {
                Version = "H003",
                Header = new H003.EbicsRequestHeader
                {
                    Static = new H003.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H003.MutableHeaderType { TransactionPhase = H003.TransactionPhaseType.Receipt },
                },
                Body = new H003.EbicsRequestBody
                {
                    TransferReceipt = new H003.EbicsRequestBodyTransferReceipt { ReceiptCode = receiptCode },
                },
            }, EbicsVersion.H003),
            EbicsVersion.H004 => EbicsXmlSerializer.SerializeToString(new H004.EbicsRequest
            {
                Version = "H004",
                Header = new H004.EbicsRequestHeader
                {
                    Static = new H004.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H004.MutableHeaderType { TransactionPhase = H004.TransactionPhaseType.Receipt },
                },
                Body = new H004.EbicsRequestBody
                {
                    TransferReceipt = new H004.EbicsRequestBodyTransferReceipt { ReceiptCode = receiptCode },
                },
            }, EbicsVersion.H004),
            EbicsVersion.H005 => EbicsXmlSerializer.SerializeToString(new H005.EbicsRequest
            {
                Version = "H005",
                Header = new H005.EbicsRequestHeader
                {
                    Static = new H005.StaticHeaderType { HostId = hostId, TransactionId = transactionId },
                    Mutable = new H005.MutableHeaderType { TransactionPhase = H005.TransactionPhaseType.Receipt },
                },
                Body = new H005.EbicsRequestBody
                {
                    TransferReceipt = new H005.EbicsRequestBodyTransferReceipt { ReceiptCode = receiptCode },
                },
            }, EbicsVersion.H005),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported EBICS version."),
        };

    // --- Order-data serializers for #29 (mirror the HIA/INI serializers above) --------------

    private static Ds.RsaKeyValueType RsaKeyValue(RsaKeyMaterial key)
    {
        var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(key);
        return new Ds.RsaKeyValueType { Modulus = modulus, Exponent = exponent };
    }

    private static Ds.X509DataType X509(X509Certificate2 certificate)
    {
        var data = new Ds.X509DataType();
        data.X509Certificate.Add(certificate.RawData);
        return data;
    }

    private static byte[] SerializeH003HsaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
        => EbicsXmlSerializer.SerializeOrderData(new H003.HsaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H003.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, PubKeyValue = new H003.PubKeyValueType { RsaKeyValue = RsaKeyValue(authKey) } },
            EncryptionPubKeyInfo = new H003.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, PubKeyValue = new H003.PubKeyValueType { RsaKeyValue = RsaKeyValue(encKey) } },
            PartnerId = partnerId,
            UserId = userId,
        });

    private static byte[] SerializeH004HsaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
        => EbicsXmlSerializer.SerializeOrderData(new H004.HsaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H004.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, PubKeyValue = new H004.PubKeyValueType { RsaKeyValue = RsaKeyValue(authKey) } },
            EncryptionPubKeyInfo = new H004.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, PubKeyValue = new H004.PubKeyValueType { RsaKeyValue = RsaKeyValue(encKey) } },
            PartnerId = partnerId,
            UserId = userId,
        });

    private static byte[] SerializeH003HcaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
        => EbicsXmlSerializer.SerializeOrderData(new H003.HcaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H003.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, PubKeyValue = new H003.PubKeyValueType { RsaKeyValue = RsaKeyValue(authKey) } },
            EncryptionPubKeyInfo = new H003.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, PubKeyValue = new H003.PubKeyValueType { RsaKeyValue = RsaKeyValue(encKey) } },
            PartnerId = partnerId,
            UserId = userId,
        });

    private static byte[] SerializeH004HcaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
        => EbicsXmlSerializer.SerializeOrderData(new H004.HcaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H004.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, PubKeyValue = new H004.PubKeyValueType { RsaKeyValue = RsaKeyValue(authKey) } },
            EncryptionPubKeyInfo = new H004.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, PubKeyValue = new H004.PubKeyValueType { RsaKeyValue = RsaKeyValue(encKey) } },
            PartnerId = partnerId,
            UserId = userId,
        });

    private static byte[] SerializeH005HcaOrderData(
        string authVersion, string encVersion, string partnerId, string userId, X509Certificate2 authCertificate, X509Certificate2 encCertificate)
        => EbicsXmlSerializer.SerializeOrderData(new H005.HcaRequestOrderDataType
        {
            AuthenticationPubKeyInfo = new H005.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, X509Data = X509(authCertificate) },
            EncryptionPubKeyInfo = new H005.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, X509Data = X509(encCertificate) },
            PartnerId = partnerId,
            UserId = userId,
        });

    private static byte[] SerializeH003HcsOrderData(
        string sigVersion, string authVersion, string encVersion, string partnerId, string userId,
        RsaKeyMaterial sigKey, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
        => EbicsXmlSerializer.SerializeOrderData(new H003.HcsRequestOrderDataType
        {
            SignaturePubKeyInfo = new S001.SignaturePubKeyInfoType { SignatureVersion = sigVersion, PubKeyValue = new S001.PubKeyValueType { RsaKeyValue = RsaKeyValue(sigKey) } },
            AuthenticationPubKeyInfo = new H003.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, PubKeyValue = new H003.PubKeyValueType { RsaKeyValue = RsaKeyValue(authKey) } },
            EncryptionPubKeyInfo = new H003.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, PubKeyValue = new H003.PubKeyValueType { RsaKeyValue = RsaKeyValue(encKey) } },
            PartnerId = partnerId,
            UserId = userId,
        });

    private static byte[] SerializeH004HcsOrderData(
        string sigVersion, string authVersion, string encVersion, string partnerId, string userId,
        RsaKeyMaterial sigKey, RsaKeyMaterial authKey, RsaKeyMaterial encKey)
        => EbicsXmlSerializer.SerializeOrderData(new H004.HcsRequestOrderDataType
        {
            SignaturePubKeyInfo = new S001.SignaturePubKeyInfoType { SignatureVersion = sigVersion, PubKeyValue = new S001.PubKeyValueType { RsaKeyValue = RsaKeyValue(sigKey) } },
            AuthenticationPubKeyInfo = new H004.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, PubKeyValue = new H004.PubKeyValueType { RsaKeyValue = RsaKeyValue(authKey) } },
            EncryptionPubKeyInfo = new H004.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, PubKeyValue = new H004.PubKeyValueType { RsaKeyValue = RsaKeyValue(encKey) } },
            PartnerId = partnerId,
            UserId = userId,
        });

    private static byte[] SerializeH005HcsOrderData(
        string sigVersion, string authVersion, string encVersion, string partnerId, string userId,
        X509Certificate2 sigCertificate, X509Certificate2 authCertificate, X509Certificate2 encCertificate)
        => EbicsXmlSerializer.SerializeOrderData(new H005.HcsRequestOrderDataType
        {
            SignaturePubKeyInfo = new S002.SignaturePubKeyInfoType { SignatureVersion = sigVersion, X509Data = X509(sigCertificate) },
            AuthenticationPubKeyInfo = new H005.AuthenticationPubKeyInfoType { AuthenticationVersion = authVersion, X509Data = X509(authCertificate) },
            EncryptionPubKeyInfo = new H005.EncryptionPubKeyInfoType { EncryptionVersion = encVersion, X509Data = X509(encCertificate) },
            PartnerId = partnerId,
            UserId = userId,
        });
}
