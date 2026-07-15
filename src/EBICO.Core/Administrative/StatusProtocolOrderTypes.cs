namespace EBICO.Core.Administrative;

/// <summary>
/// The administrative / technical status- and protocol download order types (issue #41). Unlike the
/// statement (STA/VMK/C5x) and payment (CCT/CDD/…) orders these remain classical <c>AdminOrderType</c>s
/// in H005 (they are intentionally <b>not</b> modelled as BTF services, see
/// <see cref="EBICO.Core.Btf.BtfOrderTypeCatalog"/>). They are delivered as downloads (bank→client) over
/// the download transaction and split into two families: the stammdaten/parameter orders
/// (HTD/HKD/HAA/HPD, generated from master data) and the customer-protocol orders (HAC/PTK, projected
/// from the event log).
/// </summary>
public static class StatusProtocolOrderTypes
{
    /// <summary>Customer and subscriber data of the requesting subscriber (<c>HTD</c>).</summary>
    public const string SubscriberData = "HTD";

    /// <summary>Customer data including all of the customer's subscribers (<c>HKD</c>).</summary>
    public const string CustomerData = "HKD";

    /// <summary>The order types available for download (<c>HAA</c>).</summary>
    public const string AvailableOrderTypes = "HAA";

    /// <summary>Bank parameters — access and protocol parameters (<c>HPD</c>).</summary>
    public const string BankParameters = "HPD";

    /// <summary>The machine-readable customer protocol (<c>HAC</c>), a projection over the event log.</summary>
    public const string CustomerProtocolXml = "HAC";

    /// <summary>The textual customer protocol (<c>PTK</c>), a projection over the event log.</summary>
    public const string CustomerProtocolText = "PTK";

    /// <summary>Whether <paramref name="orderType"/> is one of the status/protocol download order types.</summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for HTD/HKD/HAA/HPD/HAC/PTK; otherwise <see langword="false"/>.</returns>
    public static bool IsStatusProtocolOrderType(string? orderType)
        => IsSubscriberInfoOrderType(orderType) || IsCustomerProtocolOrderType(orderType);

    /// <summary>
    /// Whether <paramref name="orderType"/> is one of the master-data / parameter orders generated from the
    /// state store (HTD/HKD/HAA/HPD).
    /// </summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for HTD/HKD/HAA/HPD; otherwise <see langword="false"/>.</returns>
    public static bool IsSubscriberInfoOrderType(string? orderType)
        => orderType is SubscriberData or CustomerData or AvailableOrderTypes or BankParameters;

    /// <summary>
    /// Whether <paramref name="orderType"/> is one of the customer-protocol orders projected from the event
    /// log (HAC/PTK).
    /// </summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for HAC/PTK; otherwise <see langword="false"/>.</returns>
    public static bool IsCustomerProtocolOrderType(string? orderType)
        => orderType is CustomerProtocolXml or CustomerProtocolText;
}
