using EBICO.Core.Btf;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Core.Administrative;

/// <summary>
/// Builds the order data for the master-data / parameter download orders HTD, HKD, HAA and HPD (issue #41)
/// by populating the generated per-version response bindings from the emulator's domain aggregates and
/// serialising them with <see cref="EbicsXmlSerializer.SerializeOrderData"/>. The three protocol versions
/// diverge in these types (H003/H004 key on <c>OrderType</c>/<c>OrderTypes</c>, H005 on
/// <c>AdminOrderType</c>/<c>Service</c>), so each order type dispatches to a version-specific populate step.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the content is generated from the seeded master data; sub-fields that the domain
/// model does not track (order-format, transfer/authorisation levels, amount limits, X.509 parameters,
/// per-account usage restrictions) are left at their defaults or omitted. The version-specific field mapping
/// is best-effort and to be verified against the official EBICS annexes.
/// </remarks>
public static class SubscriberInfoContentBuilder
{
    /// <summary>Builds the HTD (customer and subscriber data of the requesting subscriber) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="bank">The bank the subscriber belongs to.</param>
    /// <param name="partner">The partner (customer) the subscriber belongs to.</param>
    /// <param name="user">The requesting subscriber.</param>
    /// <returns>The HTD response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHtd(EbicsVersion version, Bank bank, Partner partner, Subscriber user)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(user);

        var orderTypes = OrderTypesOf([user]);
        object root = version switch
        {
            EbicsVersion.H005 => new H005.HtdReponseOrderDataType
            {
                PartnerInfo = PartnerInfoH005(bank, partner, orderTypes),
                UserInfo = UserInfoH005(user),
            },
            EbicsVersion.H004 => new H004.HtdReponseOrderDataType
            {
                PartnerInfo = PartnerInfoH004(bank, partner, orderTypes),
                UserInfo = UserInfoH004(user),
            },
            EbicsVersion.H003 => new H003.HtdReponseOrderDataType
            {
                PartnerInfo = PartnerInfoH003(bank, partner, orderTypes),
                UserInfo = UserInfoH003(user),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    /// <summary>Builds the HKD (customer data including all of the customer's subscribers) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="bank">The bank the customer belongs to.</param>
    /// <param name="partner">The partner (customer).</param>
    /// <param name="users">All subscribers of the partner.</param>
    /// <returns>The HKD response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHkd(EbicsVersion version, Bank bank, Partner partner, IReadOnlyList<Subscriber> users)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(users);

        var orderTypes = OrderTypesOf(users);
        object root = version switch
        {
            EbicsVersion.H005 => BuildHkdH005(bank, partner, users, orderTypes),
            EbicsVersion.H004 => BuildHkdH004(bank, partner, users, orderTypes),
            EbicsVersion.H003 => BuildHkdH003(bank, partner, users, orderTypes),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    /// <summary>Builds the HAA (order types available for download) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="downloadOrderTypes">The download order-type codes available to the subscriber.</param>
    /// <returns>The HAA response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="downloadOrderTypes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHaa(EbicsVersion version, IReadOnlyList<string> downloadOrderTypes)
    {
        ArgumentNullException.ThrowIfNull(downloadOrderTypes);

        object root = version switch
        {
            EbicsVersion.H005 => BuildHaaH005(downloadOrderTypes),
            EbicsVersion.H004 => BuildHaaH004(downloadOrderTypes),
            EbicsVersion.H003 => BuildHaaH003(downloadOrderTypes),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    /// <summary>Builds the HPD (bank access and protocol parameters) order data.</summary>
    /// <param name="version">The protocol version to build for.</param>
    /// <param name="bank">The bank whose parameters are described.</param>
    /// <returns>The HPD response order data as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bank"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is undefined.</exception>
    public static byte[] BuildHpd(EbicsVersion version, Bank bank)
    {
        ArgumentNullException.ThrowIfNull(bank);

        object root = version switch
        {
            EbicsVersion.H005 => BuildHpdH005(bank),
            EbicsVersion.H004 => BuildHpdH004(bank),
            EbicsVersion.H003 => BuildHpdH003(bank),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unknown EBICS version."),
        };

        return EbicsXmlSerializer.SerializeOrderData(root);
    }

    // --- H005 ------------------------------------------------------------------------------

    private static H005.HkdResponseOrderDataType BuildHkdH005(Bank bank, Partner partner, IReadOnlyList<Subscriber> users, IReadOnlyList<string> orderTypes)
    {
        var root = new H005.HkdResponseOrderDataType { PartnerInfo = PartnerInfoH005(bank, partner, orderTypes) };
        foreach (var user in users)
        {
            root.UserInfo.Add(UserInfoH005(user));
        }

        return root;
    }

    private static H005.PartnerInfoType PartnerInfoH005(Bank bank, Partner partner, IReadOnlyList<string> orderTypes)
    {
        var info = new H005.PartnerInfoType
        {
            AddressInfo = AddressInfoH005(partner),
            BankInfo = new H005.BankInfoType { HostId = bank.HostId.Value },
        };

        foreach (var account in partner.Accounts)
        {
            var accountInfo = new H005.PartnerInfoTypeAccountInfo
            {
                AccountHolder = account.Holder,
                Currency = account.Currency,
                Description = account.Description,
                Id = account.Id,
            };
            if (account.Iban is { } iban)
            {
                accountInfo.AccountNumber.Add(new H005.AccountTypeAccountNumber { Value = iban, International = true });
            }

            if (account.Bic is { } bic)
            {
                accountInfo.BankCode.Add(new H005.AccountTypeBankCode { Value = bic, International = true });
            }

            info.AccountInfo.Add(accountInfo);
        }

        foreach (var orderType in orderTypes)
        {
            var orderInfo = new H005.AuthOrderInfoType { Description = DescriptionOf(orderType), NumSigRequired = "0" };
            if (BtfOrderTypeCatalog.TryGetBtf(orderType, out var btf))
            {
                orderInfo.Service = btf.ToRestrictedServiceType();
            }
            else
            {
                orderInfo.AdminOrderType = orderType;
            }

            info.OrderInfo.Add(orderInfo);
        }

        return info;
    }

    private static H005.AddressInfoType? AddressInfoH005(Partner partner)
    {
        var address = partner.Address;
        var name = address?.Name ?? partner.Name;
        if (address is null && name is null)
        {
            return null;
        }

        return new H005.AddressInfoType
        {
            Name = name,
            Street = address?.Street,
            PostCode = address?.PostCode,
            City = address?.City,
            Region = address?.Region,
            Country = address?.Country,
        };
    }

    private static H005.UserInfoType UserInfoH005(Subscriber user)
    {
        var info = new H005.UserInfoType
        {
            UserId = new H005.UserInfoTypeUserId { Value = user.UserId.Value, Status = UserStatus(user) },
            Name = user.Name,
        };

        foreach (var permission in user.Permissions)
        {
            var typed = new H005.UserPermissionType();
            if (BtfOrderTypeCatalog.TryGetBtf(permission.OrderType, out var btf))
            {
                typed.Service = btf.ToRestrictedServiceType();
            }
            else
            {
                typed.AdminOrderType = permission.OrderType;
            }

            info.Permission.Add(typed);
        }

        return info;
    }

    private static H005.HaaResponseOrderDataType BuildHaaH005(IReadOnlyList<string> downloadOrderTypes)
    {
        var root = new H005.HaaResponseOrderDataType();
        foreach (var orderType in downloadOrderTypes)
        {
            if (BtfOrderTypeCatalog.TryGetBtf(orderType, out var btf))
            {
                root.Service.Add(btf.ToRestrictedServiceType());
            }
        }

        return root;
    }

    private static H005.HpdResponseOrderDataType BuildHpdH005(Bank bank)
    {
        var version = new H005.HpdVersionType();
        PopulateVersions(bank, version.Protocol, version.Authentication, version.Encryption, version.Signature);

        return new H005.HpdResponseOrderDataType
        {
            AccessParams = BuildAccessParamsH005(bank),
            ProtocolParams = new H005.HpdProtocolParamsType
            {
                Version = version,
                Recovery = new H005.HpdProtocolParamsTypeRecovery { Supported = true },
                PreValidation = new H005.HpdProtocolParamsTypePreValidation { Supported = false },
                ClientDataDownload = new H005.HpdProtocolParamsTypeClientDataDownload { Supported = true },
                DownloadableOrderData = new H005.HpdProtocolParamsTypeDownloadableOrderData { Supported = true },
            },
        };
    }

    private static H005.HpdAccessParamsType BuildAccessParamsH005(Bank bank)
    {
        var access = new H005.HpdAccessParamsType { Institute = bank.Name, HostId = bank.HostId.Value };
        if (bank.Url is { } url)
        {
            access.Url.Add(new H005.HpdAccessParamsTypeUrl { Value = url });
        }

        return access;
    }

    // --- H004 ------------------------------------------------------------------------------

    private static H004.HkdResponseOrderDataType BuildHkdH004(Bank bank, Partner partner, IReadOnlyList<Subscriber> users, IReadOnlyList<string> orderTypes)
    {
        var root = new H004.HkdResponseOrderDataType { PartnerInfo = PartnerInfoH004(bank, partner, orderTypes) };
        foreach (var user in users)
        {
            root.UserInfo.Add(UserInfoH004(user));
        }

        return root;
    }

    private static H004.PartnerInfoType PartnerInfoH004(Bank bank, Partner partner, IReadOnlyList<string> orderTypes)
    {
        var info = new H004.PartnerInfoType
        {
            AddressInfo = AddressInfoH004(partner),
            BankInfo = new H004.BankInfoType { HostId = bank.HostId.Value },
        };

        foreach (var account in partner.Accounts)
        {
            var accountInfo = new H004.PartnerInfoTypeAccountInfo
            {
                AccountHolder = account.Holder,
                Currency = account.Currency,
                Description = account.Description,
                Id = account.Id,
            };
            if (account.Iban is { } iban)
            {
                accountInfo.AccountNumber.Add(new H004.AccountTypeAccountNumber { Value = iban, International = true });
            }

            if (account.Bic is { } bic)
            {
                accountInfo.BankCode.Add(new H004.AccountTypeBankCode { Value = bic, International = true });
            }

            info.AccountInfo.Add(accountInfo);
        }

        foreach (var orderType in orderTypes)
        {
            info.OrderInfo.Add(new H004.AuthOrderInfoType
            {
                OrderType = orderType,
                TransferType = IsDownloadDirection(orderType) ? H004.TransferType.Download : H004.TransferType.Upload,
                Description = DescriptionOf(orderType),
                NumSigRequired = "0",
            });
        }

        return info;
    }

    private static H004.AddressInfoType? AddressInfoH004(Partner partner)
    {
        var address = partner.Address;
        var name = address?.Name ?? partner.Name;
        if (address is null && name is null)
        {
            return null;
        }

        return new H004.AddressInfoType
        {
            Name = name,
            Street = address?.Street,
            PostCode = address?.PostCode,
            City = address?.City,
            Region = address?.Region,
            Country = address?.Country,
        };
    }

    private static H004.UserInfoType UserInfoH004(Subscriber user)
    {
        var info = new H004.UserInfoType
        {
            UserId = new H004.UserInfoTypeUserId { Value = user.UserId.Value, Status = UserStatus(user) },
            Name = user.Name,
        };

        foreach (var permission in user.Permissions)
        {
            var typed = new H004.UserPermissionType();
            typed.OrderTypes.Add(permission.OrderType);
            info.Permission.Add(typed);
        }

        return info;
    }

    private static H004.HaaResponseOrderDataType BuildHaaH004(IReadOnlyList<string> downloadOrderTypes)
    {
        var root = new H004.HaaResponseOrderDataType();
        foreach (var orderType in downloadOrderTypes)
        {
            root.OrderTypes.Add(orderType);
        }

        return root;
    }

    private static H004.HpdResponseOrderDataType BuildHpdH004(Bank bank)
    {
        var version = new H004.HpdVersionType();
        PopulateVersions(bank, version.Protocol, version.Authentication, version.Encryption, version.Signature);

        return new H004.HpdResponseOrderDataType
        {
            AccessParams = BuildAccessParamsH004(bank),
            ProtocolParams = new H004.HpdProtocolParamsType
            {
                Version = version,
                Recovery = new H004.HpdProtocolParamsTypeRecovery { Supported = true },
                PreValidation = new H004.HpdProtocolParamsTypePreValidation { Supported = false },
                ClientDataDownload = new H004.HpdProtocolParamsTypeClientDataDownload { Supported = true },
                DownloadableOrderData = new H004.HpdProtocolParamsTypeDownloadableOrderData { Supported = true },
            },
        };
    }

    private static H004.HpdAccessParamsType BuildAccessParamsH004(Bank bank)
    {
        var access = new H004.HpdAccessParamsType { Institute = bank.Name, HostId = bank.HostId.Value };
        if (bank.Url is { } url)
        {
            access.Url.Add(new H004.HpdAccessParamsTypeUrl { Value = url });
        }

        return access;
    }

    // --- H003 ------------------------------------------------------------------------------

    private static H003.HkdResponseOrderDataType BuildHkdH003(Bank bank, Partner partner, IReadOnlyList<Subscriber> users, IReadOnlyList<string> orderTypes)
    {
        var root = new H003.HkdResponseOrderDataType { PartnerInfo = PartnerInfoH003(bank, partner, orderTypes) };
        foreach (var user in users)
        {
            root.UserInfo.Add(UserInfoH003(user));
        }

        return root;
    }

    private static H003.PartnerInfoType PartnerInfoH003(Bank bank, Partner partner, IReadOnlyList<string> orderTypes)
    {
        var info = new H003.PartnerInfoType
        {
            AddressInfo = AddressInfoH003(partner),
            BankInfo = new H003.BankInfoType { HostId = bank.HostId.Value },
        };

        foreach (var account in partner.Accounts)
        {
            var accountInfo = new H003.PartnerInfoTypeAccountInfo
            {
                AccountHolder = account.Holder,
                Currency = account.Currency,
                Description = account.Description,
                Id = account.Id,
            };
            if (account.Iban is { } iban)
            {
                accountInfo.AccountNumber.Add(new H003.AccountTypeAccountNumber { Value = iban, International = true });
            }

            if (account.Bic is { } bic)
            {
                accountInfo.BankCode.Add(new H003.AccountTypeBankCode { Value = bic, International = true });
            }

            info.AccountInfo.Add(accountInfo);
        }

        foreach (var orderType in orderTypes)
        {
            info.OrderInfo.Add(new H003.AuthOrderInfoType
            {
                OrderType = orderType,
                TransferType = IsDownloadDirection(orderType) ? H003.TransferType.Download : H003.TransferType.Upload,
                Description = DescriptionOf(orderType),
                NumSigRequired = "0",
            });
        }

        return info;
    }

    private static H003.AddressInfoType? AddressInfoH003(Partner partner)
    {
        var address = partner.Address;
        var name = address?.Name ?? partner.Name;
        if (address is null && name is null)
        {
            return null;
        }

        return new H003.AddressInfoType
        {
            Name = name,
            Street = address?.Street,
            PostCode = address?.PostCode,
            City = address?.City,
            Region = address?.Region,
            Country = address?.Country,
        };
    }

    private static H003.UserInfoType UserInfoH003(Subscriber user)
    {
        var info = new H003.UserInfoType
        {
            UserId = new H003.UserInfoTypeUserId { Value = user.UserId.Value, Status = UserStatus(user) },
            Name = user.Name,
        };

        foreach (var permission in user.Permissions)
        {
            var typed = new H003.UserPermissionType();
            typed.OrderTypes.Add(permission.OrderType);
            info.Permission.Add(typed);
        }

        return info;
    }

    private static H003.HaaResponseOrderDataType BuildHaaH003(IReadOnlyList<string> downloadOrderTypes)
    {
        var root = new H003.HaaResponseOrderDataType();
        foreach (var orderType in downloadOrderTypes)
        {
            root.OrderTypes.Add(orderType);
        }

        return root;
    }

    private static H003.HpdResponseOrderDataType BuildHpdH003(Bank bank)
    {
        var version = new H003.HpdVersionType();
        PopulateVersions(bank, version.Protocol, version.Authentication, version.Encryption, version.Signature);

        return new H003.HpdResponseOrderDataType
        {
            AccessParams = BuildAccessParamsH003(bank),
            ProtocolParams = new H003.HpdProtocolParamsType
            {
                Version = version,
                Recovery = new H003.HpdProtocolParamsTypeRecovery { Supported = true },
                PreValidation = new H003.HpdProtocolParamsTypePreValidation { Supported = false },
                ClientDataDownload = new H003.HpdProtocolParamsTypeClientDataDownload { Supported = true },
                DownloadableOrderData = new H003.HpdProtocolParamsTypeDownloadableOrderData { Supported = true },
            },
        };
    }

    private static H003.HpdAccessParamsType BuildAccessParamsH003(Bank bank)
    {
        var access = new H003.HpdAccessParamsType { Institute = bank.Name, HostId = bank.HostId.Value };
        if (bank.Url is { } url)
        {
            access.Url.Add(new H003.HpdAccessParamsTypeUrl { Value = url });
        }

        return access;
    }

    // --- Shared helpers --------------------------------------------------------------------

    // The distinct order-type codes across the given subscribers' permissions (sorted for determinism).
    private static IReadOnlyList<string> OrderTypesOf(IReadOnlyList<Subscriber> users)
        => users
            .SelectMany(u => u.Permissions.Select(p => p.OrderType))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(o => o, StringComparer.Ordinal)
            .ToArray();

    // Whether an order type is a download (bank->client) — used to set the H003/H004 TransferType.
    private static bool IsDownloadDirection(string orderType)
        => orderType is "FDL" or "BTD"
            || BtfOrderTypeCatalog.IsDownloadOrderType(orderType)
            || StatusProtocolOrderTypes.IsStatusProtocolOrderType(orderType);

    // A short description for an order type: the BTF catalog description when known, a fixed description for
    // the status/protocol admin orders, otherwise the code itself.
    private static string DescriptionOf(string orderType)
    {
        foreach (var entry in BtfOrderTypeCatalog.All)
        {
            if (string.Equals(entry.OrderType, orderType, StringComparison.Ordinal))
            {
                return entry.Description;
            }
        }

        return orderType switch
        {
            StatusProtocolOrderTypes.SubscriberData => "Download customer and subscriber data",
            StatusProtocolOrderTypes.CustomerData => "Download customer data",
            StatusProtocolOrderTypes.AvailableOrderTypes => "Download available order types",
            StatusProtocolOrderTypes.BankParameters => "Download bank parameters",
            StatusProtocolOrderTypes.CustomerProtocolXml => "Download customer protocol",
            StatusProtocolOrderTypes.CustomerProtocolText => "Download customer protocol (text)",
            _ => orderType,
        };
    }

    // A plausible EBICS user status byte derived from the subscriber lifecycle state (Spec-Vorbehalt).
    private static byte UserStatus(Subscriber user) => user.State switch
    {
        SubscriberState.Ready => 5,
        SubscriberState.Initialized => 2,
        SubscriberState.Suspended => 5,
        _ => 1,
    };

    // Fills the HPD version lists from the bank's supported versions and the emulator's fixed key versions.
    private static void PopulateVersions(
        Bank bank,
        ICollection<string> protocol,
        ICollection<string> authentication,
        ICollection<string> encryption,
        ICollection<string> signature)
    {
        foreach (var supported in bank.SupportedVersions)
        {
            protocol.Add(EbicsVersions.Get(supported).Code);
        }

        authentication.Add("X002");
        encryption.Add("E002");
        signature.Add("A005");
        signature.Add("A006");
    }
}
