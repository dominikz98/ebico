using System.Globalization;
using EBICO.Core.Btf;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Core.Administrative;

/// <summary>
/// Builds the order data for the distributed-electronic-signature download orders HVU/HVZ (overview) and
/// HVD/HVT (detail) — issue #42 — by populating the generated per-version response bindings from a
/// version-neutral <see cref="VeuOrderView"/> and serialising them with
/// <see cref="EbicsXmlSerializer.SerializeOrderData"/>. The three protocol versions diverge in the order
/// identification (H003/H004 carry the classical <c>OrderType</c>, H005 the BTF <c>Service</c>), so each
/// order type dispatches to a version-specific populate step (mirrors
/// <see cref="SubscriberInfoContentBuilder"/>).
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the content is projected from the emulator's open-order state; the electronic
/// signatures themselves are not verified and the digest is a plain SHA-256 over the order data rather than
/// the canonical EBICS ES hash. HVT is rendered order-summary only (a single <c>OrderInfo</c> carrying the
/// message name), not a full ISO 20022 per-transaction breakdown. The exact per-version field mapping is
/// best-effort and to be verified against the official EBICS annexes.
/// </remarks>
public static class VeuResponseBuilder
{
    // The signature algorithm version stamped on the data digest (Spec-Vorbehalt: fixed placeholder).
    private const string SignatureVersion = "A006";

    /// <summary>Builds the HVU (overview of orders awaiting distributed signatures) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="orders">The orders currently awaiting signatures (may be empty).</param>
    /// <returns>The HVU response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="orders"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHvu(EbicsVersion version, IReadOnlyList<VeuOrderView> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        object root = version switch
        {
            EbicsVersion.H005 => BuildHvuH005(orders),
            EbicsVersion.H004 => BuildHvuH004(orders),
            EbicsVersion.H003 => BuildHvuH003(orders),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    /// <summary>Builds the HVZ (overview with additional details) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="orders">The orders currently awaiting signatures (may be empty).</param>
    /// <returns>The HVZ response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="orders"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHvz(EbicsVersion version, IReadOnlyList<VeuOrderView> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        object root = version switch
        {
            EbicsVersion.H005 => BuildHvzH005(orders),
            EbicsVersion.H004 => BuildHvzH004(orders),
            EbicsVersion.H003 => BuildHvzH003(orders),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    /// <summary>Builds the HVD (status/detail of a single awaiting order) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="order">The order whose status is requested.</param>
    /// <returns>The HVD response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHvd(EbicsVersion version, VeuOrderView order)
    {
        ArgumentNullException.ThrowIfNull(order);

        object root = version switch
        {
            EbicsVersion.H005 => BuildHvdH005(order),
            EbicsVersion.H004 => BuildHvdH004(order),
            EbicsVersion.H003 => BuildHvdH003(order),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    /// <summary>Builds the HVT (transaction details of a single awaiting order) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="order">The order whose transaction details are requested.</param>
    /// <returns>The HVT response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHvt(EbicsVersion version, VeuOrderView order)
    {
        ArgumentNullException.ThrowIfNull(order);

        object root = version switch
        {
            EbicsVersion.H005 => BuildHvtH005(order),
            EbicsVersion.H004 => BuildHvtH004(order),
            EbicsVersion.H003 => BuildHvtH003(order),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    // --- H005 ------------------------------------------------------------------------------

    private static H005.HvuResponseOrderDataType BuildHvuH005(IReadOnlyList<VeuOrderView> orders)
    {
        var root = new H005.HvuResponseOrderDataType();
        foreach (var order in orders)
        {
            var details = new H005.HvuOrderDetailsType
            {
                OrderId = order.OrderId,
                OrderDataSize = Size(order),
                SigningInfo = SigningInfoH005(order),
                OriginatorInfo = OriginatorH005(order.Originator),
                AdditionalOrderInfo = order.AdditionalOrderInfo,
            };
            SetServiceH005(order, s => details.Service = s);
            foreach (var signer in order.Signers)
            {
                details.SignerInfo.Add(SignerH005(signer));
            }

            root.OrderDetails.Add(details);
        }

        return root;
    }

    private static H005.HvzResponseOrderDataType BuildHvzH005(IReadOnlyList<VeuOrderView> orders)
    {
        var root = new H005.HvzResponseOrderDataType();
        foreach (var order in orders)
        {
            var details = new H005.HvzOrderDetailsType
            {
                OrderId = order.OrderId,
                DataDigest = DigestH005(order),
                OrderDataAvailable = false,
                OrderDataSize = Size(order),
                OrderDetailsAvailable = true,
                SigningInfo = SigningInfoH005(order),
                OriginatorInfo = OriginatorH005(order.Originator),
                AdditionalOrderInfo = order.AdditionalOrderInfo,
            };
            SetServiceH005(order, s => details.Service = s);
            foreach (var signer in order.Signers)
            {
                details.SignerInfo.Add(SignerH005(signer));
            }

            root.OrderDetails.Add(details);
        }

        return root;
    }

    private static H005.HvdResponseOrderDataType BuildHvdH005(VeuOrderView order)
    {
        var root = new H005.HvdResponseOrderDataType
        {
            DataDigest = DigestH005(order),
            OrderDataAvailable = false,
            OrderDataSize = Size(order),
            OrderDetailsAvailable = true,
        };
        foreach (var signer in order.Signers)
        {
            root.SignerInfo.Add(SignerH005(signer));
        }

        return root;
    }

    private static H005.HvtResponseOrderData BuildHvtH005(VeuOrderView order)
    {
        var root = new H005.HvtResponseOrderData { NumOrderInfos = 1 };
        var info = new H005.HvtOrderInfoType();
        if (MessageNameOf(order) is { } messageName)
        {
            info.MsgName = new H005.MessageType { Value = messageName };
        }

        root.OrderInfo.Add(info);
        return root;
    }

    private static void SetServiceH005(VeuOrderView order, Action<H005.RestrictedServiceType> set)
    {
        if (BtfOrderTypeCatalog.TryGetBtf(order.OrderType, out var btf))
        {
            set(btf.ToRestrictedServiceType());
        }
    }

    private static H005.HvuSigningInfoType SigningInfoH005(VeuOrderView order) => new()
    {
        ReadyToBeSigned = order.ReadyToBeSigned,
        NumSigRequired = order.NumSigRequired.ToString(CultureInfo.InvariantCulture),
        NumSigDone = order.NumSigDone.ToString(CultureInfo.InvariantCulture),
    };

    private static H005.SignerInfoType SignerH005(VeuSignerView signer)
    {
        var info = new H005.SignerInfoType
        {
            PartnerId = signer.PartnerId,
            UserId = signer.UserId,
            Name = signer.Name,
            Timestamp = signer.Timestamp.UtcDateTime,
        };
        if (signer.Permission is { } permission)
        {
            info.Permission = new H005.SignerInfoTypePermission { AuthorisationLevel = AuthLevelH005(permission) };
        }

        return info;
    }

    private static H005.HvuOriginatorInfoType OriginatorH005(VeuSignerView originator) => new()
    {
        PartnerId = originator.PartnerId,
        UserId = originator.UserId,
        Name = originator.Name,
        Timestamp = originator.Timestamp.UtcDateTime,
    };

    private static H005.DataDigestType DigestH005(VeuOrderView order)
        => new() { Value = order.DataDigest, SignatureVersion = SignatureVersion };

    private static H005.AuthorisationLevelType AuthLevelH005(SignatureClass signatureClass) => signatureClass switch
    {
        SignatureClass.E => H005.AuthorisationLevelType.E,
        SignatureClass.A => H005.AuthorisationLevelType.A,
        SignatureClass.B => H005.AuthorisationLevelType.B,
        _ => H005.AuthorisationLevelType.T,
    };

    // --- H004 ------------------------------------------------------------------------------

    private static H004.HvuResponseOrderDataType BuildHvuH004(IReadOnlyList<VeuOrderView> orders)
    {
        var root = new H004.HvuResponseOrderDataType();
        foreach (var order in orders)
        {
            var details = new H004.HvuOrderDetailsType
            {
                OrderType = order.OrderType,
                OrderId = order.OrderId,
                OrderDataSize = Size(order),
                SigningInfo = SigningInfoH004(order),
                OriginatorInfo = OriginatorH004(order.Originator),
            };
            foreach (var signer in order.Signers)
            {
                details.SignerInfo.Add(SignerH004(signer));
            }

            root.OrderDetails.Add(details);
        }

        return root;
    }

    private static H004.HvzResponseOrderDataType BuildHvzH004(IReadOnlyList<VeuOrderView> orders)
    {
        var root = new H004.HvzResponseOrderDataType();
        foreach (var order in orders)
        {
            var details = new H004.HvzOrderDetailsType
            {
                OrderType = order.OrderType,
                OrderId = order.OrderId,
                DataDigest = DigestH004(order),
                OrderDataAvailable = false,
                OrderDataSize = Size(order),
                OrderDetailsAvailable = true,
                SigningInfo = SigningInfoH004(order),
                OriginatorInfo = OriginatorH004(order.Originator),
            };
            foreach (var signer in order.Signers)
            {
                details.SignerInfo.Add(SignerH004(signer));
            }

            root.OrderDetails.Add(details);
        }

        return root;
    }

    private static H004.HvdResponseOrderDataType BuildHvdH004(VeuOrderView order)
    {
        var root = new H004.HvdResponseOrderDataType
        {
            DataDigest = DigestH004(order),
            OrderDataAvailable = false,
            OrderDataSize = Size(order),
            OrderDetailsAvailable = true,
        };
        foreach (var signer in order.Signers)
        {
            root.SignerInfo.Add(SignerH004(signer));
        }

        return root;
    }

    private static H004.HvtResponseOrderData BuildHvtH004(VeuOrderView order)
    {
        var root = new H004.HvtResponseOrderData { NumOrderInfos = 1 };
        root.OrderInfo.Add(new H004.HvtOrderInfoType { OrderFormat = MessageNameOf(order) });
        return root;
    }

    private static H004.HvuSigningInfoType SigningInfoH004(VeuOrderView order) => new()
    {
        ReadyToBeSigned = order.ReadyToBeSigned,
        NumSigRequired = order.NumSigRequired.ToString(CultureInfo.InvariantCulture),
        NumSigDone = order.NumSigDone.ToString(CultureInfo.InvariantCulture),
    };

    private static H004.SignerInfoType SignerH004(VeuSignerView signer)
    {
        var info = new H004.SignerInfoType
        {
            PartnerId = signer.PartnerId,
            UserId = signer.UserId,
            Name = signer.Name,
            Timestamp = signer.Timestamp.UtcDateTime,
        };
        if (signer.Permission is { } permission)
        {
            info.Permission = new H004.SignerInfoTypePermission { AuthorisationLevel = AuthLevelH004(permission) };
        }

        return info;
    }

    private static H004.HvuOriginatorInfoType OriginatorH004(VeuSignerView originator) => new()
    {
        PartnerId = originator.PartnerId,
        UserId = originator.UserId,
        Name = originator.Name,
        Timestamp = originator.Timestamp.UtcDateTime,
    };

    private static H004.DataDigestType DigestH004(VeuOrderView order)
        => new() { Value = order.DataDigest, SignatureVersion = SignatureVersion };

    private static H004.AuthorisationLevelType AuthLevelH004(SignatureClass signatureClass) => signatureClass switch
    {
        SignatureClass.E => H004.AuthorisationLevelType.E,
        SignatureClass.A => H004.AuthorisationLevelType.A,
        SignatureClass.B => H004.AuthorisationLevelType.B,
        _ => H004.AuthorisationLevelType.T,
    };

    // --- H003 ------------------------------------------------------------------------------

    private static H003.HvuResponseOrderDataType BuildHvuH003(IReadOnlyList<VeuOrderView> orders)
    {
        var root = new H003.HvuResponseOrderDataType();
        foreach (var order in orders)
        {
            var details = new H003.HvuOrderDetailsType
            {
                OrderType = order.OrderType,
                OrderId = order.OrderId,
                OrderDataSize = Size(order),
                SigningInfo = SigningInfoH003(order),
                OriginatorInfo = OriginatorH003(order.Originator),
            };
            foreach (var signer in order.Signers)
            {
                details.SignerInfo.Add(SignerH003(signer));
            }

            root.OrderDetails.Add(details);
        }

        return root;
    }

    private static H003.HvzResponseOrderDataType BuildHvzH003(IReadOnlyList<VeuOrderView> orders)
    {
        var root = new H003.HvzResponseOrderDataType();
        foreach (var order in orders)
        {
            var details = new H003.HvzOrderDetailsType
            {
                OrderType = order.OrderType,
                OrderId = order.OrderId,
                DataDigest = DigestH003(order),
                OrderDataAvailable = false,
                OrderDataSize = Size(order),
                OrderDetailsAvailable = true,
                SigningInfo = SigningInfoH003(order),
                OriginatorInfo = OriginatorH003(order.Originator),
            };
            foreach (var signer in order.Signers)
            {
                details.SignerInfo.Add(SignerH003(signer));
            }

            root.OrderDetails.Add(details);
        }

        return root;
    }

    private static H003.HvdResponseOrderDataType BuildHvdH003(VeuOrderView order)
    {
        var root = new H003.HvdResponseOrderDataType
        {
            DataDigest = DigestH003(order),
            OrderDataAvailable = false,
            OrderDataSize = Size(order),
            OrderDetailsAvailable = true,
        };
        foreach (var signer in order.Signers)
        {
            root.SignerInfo.Add(SignerH003(signer));
        }

        return root;
    }

    private static H003.HvtResponseOrderData BuildHvtH003(VeuOrderView order)
    {
        var root = new H003.HvtResponseOrderData { NumOrderInfos = 1 };
        root.OrderInfo.Add(new H003.HvtOrderInfoType { OrderFormat = MessageNameOf(order) });
        return root;
    }

    private static H003.HvuSigningInfoType SigningInfoH003(VeuOrderView order) => new()
    {
        ReadyToBeSigned = order.ReadyToBeSigned,
        NumSigRequired = order.NumSigRequired.ToString(CultureInfo.InvariantCulture),
        NumSigDone = order.NumSigDone.ToString(CultureInfo.InvariantCulture),
    };

    private static H003.SignerInfoType SignerH003(VeuSignerView signer)
    {
        var info = new H003.SignerInfoType
        {
            PartnerId = signer.PartnerId,
            UserId = signer.UserId,
            Name = signer.Name,
            Timestamp = signer.Timestamp.UtcDateTime,
        };
        if (signer.Permission is { } permission)
        {
            info.Permission = new H003.SignerInfoTypePermission { AuthorisationLevel = AuthLevelH003(permission) };
        }

        return info;
    }

    private static H003.HvuOriginatorInfoType OriginatorH003(VeuSignerView originator) => new()
    {
        PartnerId = originator.PartnerId,
        UserId = originator.UserId,
        Name = originator.Name,
        Timestamp = originator.Timestamp.UtcDateTime,
    };

    private static H003.DataDigestType DigestH003(VeuOrderView order)
        => new() { Value = order.DataDigest, SignatureVersion = SignatureVersion };

    private static H003.AuthorisationLevelType AuthLevelH003(SignatureClass signatureClass) => signatureClass switch
    {
        SignatureClass.E => H003.AuthorisationLevelType.E,
        SignatureClass.A => H003.AuthorisationLevelType.A,
        SignatureClass.B => H003.AuthorisationLevelType.B,
        _ => H003.AuthorisationLevelType.T,
    };

    // --- Shared helpers --------------------------------------------------------------------

    // The order-data size as the schema's string-typed byte count.
    private static string Size(VeuOrderView order) => order.OrderDataSize.ToString(CultureInfo.InvariantCulture);

    // The ISO/SWIFT message-name family of the underlying order (e.g. "pain.001"), taken from the BTF catalog;
    // null when the order type is not a catalogued (payment) type.
    private static string? MessageNameOf(VeuOrderView order)
        => BtfOrderTypeCatalog.TryGetBtf(order.OrderType, out var btf) ? btf.MessageName : null;
}
