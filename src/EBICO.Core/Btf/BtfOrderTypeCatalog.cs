using EBICO.Core.Schema.H005;

namespace EBICO.Core.Btf;

/// <summary>The transfer direction a <see cref="BtfMapping"/> applies to.</summary>
public enum BtfDirection
{
    /// <summary>A client-to-bank upload (H005 <c>BTU</c>).</summary>
    Upload,

    /// <summary>A bank-to-client download (H005 <c>BTD</c>).</summary>
    Download,

    /// <summary>Applicable to both directions.</summary>
    Both,
}

/// <summary>
/// A single entry of the <see cref="BtfOrderTypeCatalog"/>: the equivalence between a classical
/// H003/H004 order type and an H005 <see cref="Btf"/>.
/// </summary>
/// <param name="OrderType">The classical order type code (e.g. <c>"CCT"</c>, <c>"STA"</c>).</param>
/// <param name="Btf">The equivalent H005 business transaction format.</param>
/// <param name="Direction">The transfer direction the mapping applies to.</param>
/// <param name="Description">A short human-readable description of the business transaction.</param>
public sealed record BtfMapping(string OrderType, BusinessTransactionFormat Btf, BtfDirection Direction, string Description);

/// <summary>
/// Maps H005 business transaction formats to their classical H003/H004 order-type codes and back. This is
/// the <b>framework</b> table (issue #38) on which the concrete order implementations (#39–#42) build; it
/// carries a representative, best-effort seed of the common payment and statement orders and is extended
/// by those issues. The authoritative source is the proprietary EBICS <i>BTF-Mapping / External Code
/// List</i>, which is not committed to the repository — the seeded codes are therefore best-effort and
/// verified against the official list as the concrete orders land (see <c>docs/server/btf-framework.md</c>).
/// </summary>
/// <remarks>
/// Administrative/technical order types (e.g. <c>HAC</c>, <c>HTD</c>, <c>HKD</c>, <c>HPD</c>, <c>PTK</c>)
/// remain <c>AdminOrderType</c>s in H005 and are intentionally not modelled as BTF services here; they are
/// covered by issue #41.
/// </remarks>
public static class BtfOrderTypeCatalog
{
    // Representative, best-effort seed (issue #38). Extended by #39–#43 against the official BTF-Mapping list.
    private static readonly BtfMapping[] Entries =
    [
        // --- Uploads (BTU) ---
        new("CCT", new BusinessTransactionFormat("SCT", messageName: "pain.001"), BtfDirection.Upload, "SEPA Credit Transfer"),
        new("CIP", new BusinessTransactionFormat("SCT", option: "INST", messageName: "pain.001"), BtfDirection.Upload, "SEPA Instant Credit Transfer"),
        new("CDD", new BusinessTransactionFormat("SDD", option: "COR", messageName: "pain.008"), BtfDirection.Upload, "SEPA Direct Debit (CORE)"),
        new("CDB", new BusinessTransactionFormat("SDD", option: "B2B", messageName: "pain.008"), BtfDirection.Upload, "SEPA Direct Debit (B2B)"),

        // --- Downloads (BTD) ---
        new("STA", new BusinessTransactionFormat("EOP", container: ContainerStringType.Zip, messageName: "mt940"), BtfDirection.Download, "Statement (SWIFT MT940)"),
        new("C53", new BusinessTransactionFormat("EOP", container: ContainerStringType.Zip, messageName: "camt.053"), BtfDirection.Download, "Bank-to-Customer Statement (camt.053)"),
        new("C52", new BusinessTransactionFormat("STM", container: ContainerStringType.Zip, messageName: "camt.052"), BtfDirection.Download, "Bank-to-Customer Account Report (camt.052)"),
        new("C54", new BusinessTransactionFormat("EOP", container: ContainerStringType.Zip, messageName: "camt.054"), BtfDirection.Download, "Debit/Credit Notification (camt.054)"),
    ];

    /// <summary>All catalog entries (for documentation, the Suite and the coverage matrix in issue #43).</summary>
    public static IReadOnlyList<BtfMapping> All => Entries;

    /// <summary>Resolves the H005 business transaction format for a classical order-type code.</summary>
    /// <param name="orderType">The classical order type code (e.g. <c>"CCT"</c>).</param>
    /// <param name="btf">The equivalent BTF when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a mapping exists; otherwise <see langword="false"/>.</returns>
    public static bool TryGetBtf(string orderType, out BusinessTransactionFormat btf)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.OrderType, orderType, StringComparison.Ordinal))
            {
                btf = entry.Btf;
                return true;
            }
        }

        btf = default;
        return false;
    }

    /// <summary>
    /// Resolves the classical order-type code for a business transaction format, matching on the service
    /// code, the service option and the message-name family (ignoring the ISO version/variant and the
    /// container).
    /// </summary>
    /// <param name="btf">The business transaction format to look up.</param>
    /// <param name="orderType">The classical order type code when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a mapping exists; otherwise <see langword="false"/>.</returns>
    public static bool TryGetOrderType(BusinessTransactionFormat btf, out string orderType)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Btf.Service, btf.Service, StringComparison.Ordinal)
                && OptionMatches(entry.Btf.Option, btf.Option)
                && MessageNameMatches(entry.Btf.MessageName, btf.MessageName))
            {
                orderType = entry.OrderType;
                return true;
            }
        }

        orderType = string.Empty;
        return false;
    }

    /// <summary>
    /// Computes the effective authorisation key for a request. When a <paramref name="btf"/> is present
    /// (H005 <c>BTU</c>/<c>BTD</c>) it is mapped to its classical order-type code (falling back to
    /// <see cref="BusinessTransactionFormat.CanonicalKey"/> when unmapped); otherwise the raw
    /// <paramref name="adminOrderType"/> is used (the classical order type for H003/H004, or
    /// <c>"BTU"</c>/<c>"BTD"</c> for an H005 request that carries no BTF service).
    /// </summary>
    /// <param name="adminOrderType">The extracted order/admin-order type, or <see langword="null"/>.</param>
    /// <param name="btf">The extracted BTF, or <see langword="null"/> when absent.</param>
    /// <returns>The effective order-type key, or <see langword="null"/> when neither is available.</returns>
    public static string? ResolveOrderType(string? adminOrderType, BusinessTransactionFormat? btf)
    {
        if (btf is { } value)
        {
            return TryGetOrderType(value, out var orderType) ? orderType : value.CanonicalKey;
        }

        return string.IsNullOrWhiteSpace(adminOrderType) ? null : adminOrderType;
    }

    /// <summary>
    /// Whether <paramref name="orderType"/> is a classical order-type code that maps to an <b>upload</b>
    /// business transaction (<see cref="BtfDirection.Upload"/> or <see cref="BtfDirection.Both"/>) — the
    /// payment order types (CCT/CIP/CDD/CDB) a client may submit <i>directly</i> (H003/H004) instead of
    /// through the generic <c>FUL</c>/<c>BTU</c>. Used by the upload routing to recognise direct codes.
    /// </summary>
    /// <param name="orderType">The classical order type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for a direct upload order-type code; otherwise <see langword="false"/>.</returns>
    public static bool IsUploadOrderType(string? orderType)
    {
        if (string.IsNullOrWhiteSpace(orderType))
        {
            return false;
        }

        foreach (var entry in Entries)
        {
            if (entry.Direction is BtfDirection.Upload or BtfDirection.Both
                && string.Equals(entry.OrderType, orderType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the classical order-type code for a generic file upload (<c>FUL</c>) by its
    /// <c>FULOrderParams/FileFormat</c> value (e.g. <c>"pain.001.001.09"</c>), matching on the message-name
    /// family of the catalog's upload entries. When several upload entries share a message family
    /// (e.g. <c>CDD</c>/<c>CDB</c> both carry <c>pain.008</c>) the first — the un-optioned default
    /// (<c>CCT</c>, <c>CDD</c>) — wins; the service option (INST/B2B) cannot be told apart from the file
    /// format alone (best-effort, see <c>docs/server/payment-orders.md</c>).
    /// </summary>
    /// <param name="fileFormat">The <c>FileFormat</c> value (e.g. <c>"pain.008.001.02"</c>).</param>
    /// <param name="orderType">The classical order type code when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the file format maps to an upload order type; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileFormat"/> is <see langword="null"/>.</exception>
    public static bool TryGetOrderTypeByFileFormat(string fileFormat, out string orderType)
    {
        ArgumentNullException.ThrowIfNull(fileFormat);

        foreach (var entry in Entries)
        {
            if (entry.Direction is BtfDirection.Upload or BtfDirection.Both
                && entry.Btf.MessageName is { } messageName
                && MessageNameMatches(messageName, fileFormat))
            {
                orderType = entry.OrderType;
                return true;
            }
        }

        orderType = string.Empty;
        return false;
    }

    /// <summary>
    /// Resolves the effective classical order-type code for an <b>upload</b> across the three submission
    /// conventions, in precedence order: the H005 <paramref name="btf"/> (<c>BTU</c>), a generic
    /// <c>FUL</c> with a <paramref name="fileFormat"/> (H003/H004), or a classical order type submitted
    /// directly. Falls back to the raw <paramref name="orderType"/> when no more specific mapping applies
    /// (mirrors <see cref="ResolveOrderType"/> for the download side).
    /// </summary>
    /// <param name="orderType">The extracted order/admin-order type (e.g. <c>"CCT"</c>, <c>"FUL"</c>, <c>"BTU"</c>), or <see langword="null"/>.</param>
    /// <param name="btf">The extracted H005 BTF, or <see langword="null"/> when absent.</param>
    /// <param name="fileFormat">The H003/H004 <c>FULOrderParams/FileFormat</c> value, or <see langword="null"/> when absent.</param>
    /// <returns>The effective upload order-type code, or <see langword="null"/> when nothing is available.</returns>
    public static string? ResolveUploadOrderType(string? orderType, BusinessTransactionFormat? btf, string? fileFormat)
    {
        if (btf is { } value)
        {
            return TryGetOrderType(value, out var mapped) ? mapped : value.CanonicalKey;
        }

        if (!string.IsNullOrWhiteSpace(fileFormat) && TryGetOrderTypeByFileFormat(fileFormat, out var byFormat))
        {
            return byFormat;
        }

        return string.IsNullOrWhiteSpace(orderType) ? null : orderType;
    }

    private static bool OptionMatches(string? mappingOption, string? candidateOption)
        => string.Equals(mappingOption, candidateOption, StringComparison.Ordinal);

    private static bool MessageNameMatches(string? mappingMessageName, string? candidateMessageName)
    {
        if (mappingMessageName is null)
        {
            return candidateMessageName is null;
        }

        if (candidateMessageName is null)
        {
            return false;
        }

        // Accept the family: a seeded "camt.053" also matches an incoming "camt.053.001.08".
        return string.Equals(candidateMessageName, mappingMessageName, StringComparison.Ordinal)
            || candidateMessageName.StartsWith(mappingMessageName + ".", StringComparison.Ordinal);
    }
}
