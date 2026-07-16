namespace EBICO.Core.Administrative;

/// <summary>
/// The order types of the distributed electronic signature (EDS / "verteilte elektronische Unterschrift",
/// VEU — issue #42). Like the status/protocol orders (issue #41) these remain classical
/// <c>AdminOrderType</c>s in H005 and are intentionally <b>not</b> modelled as BTF services (see
/// <see cref="EBICO.Core.Btf.BtfOrderTypeCatalog"/>). They split into the download orders that project the
/// open-order state (HVU/HVZ overview, HVD/HVT detail) and the upload orders that mutate it (HVE add a
/// signature, HVS cancel/reject an order).
/// </summary>
public static class VeuOrderTypes
{
    /// <summary>Overview of the orders awaiting distributed signatures (<c>HVU</c>, download).</summary>
    public const string Overview = "HVU";

    /// <summary>Overview of the awaiting orders with additional (payment) details (<c>HVZ</c>, download).</summary>
    public const string OverviewWithDetails = "HVZ";

    /// <summary>Status/detail of a single awaiting order (<c>HVD</c>, download).</summary>
    public const string Detail = "HVD";

    /// <summary>Transaction details of a single awaiting order (<c>HVT</c>, download).</summary>
    public const string TransactionDetail = "HVT";

    /// <summary>Add an electronic signature to an awaiting order (<c>HVE</c>, upload).</summary>
    public const string AddSignature = "HVE";

    /// <summary>Cancel/reject an awaiting order (<c>HVS</c>, upload).</summary>
    public const string CancelOrder = "HVS";

    /// <summary>Whether <paramref name="orderType"/> is one of the VEU download order types (HVU/HVZ/HVD/HVT).</summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for HVU/HVZ/HVD/HVT; otherwise <see langword="false"/>.</returns>
    public static bool IsVeuDownloadOrderType(string? orderType)
        => orderType is Overview or OverviewWithDetails or Detail or TransactionDetail;

    /// <summary>Whether <paramref name="orderType"/> is one of the VEU upload order types (HVE/HVS).</summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for HVE/HVS; otherwise <see langword="false"/>.</returns>
    public static bool IsVeuUploadOrderType(string? orderType)
        => orderType is AddSignature or CancelOrder;

    /// <summary>Whether <paramref name="orderType"/> is any VEU order type (HVU/HVZ/HVD/HVT/HVE/HVS).</summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for any VEU order type; otherwise <see langword="false"/>.</returns>
    public static bool IsVeuOrderType(string? orderType)
        => IsVeuDownloadOrderType(orderType) || IsVeuUploadOrderType(orderType);
}
