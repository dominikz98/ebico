namespace EBICO.Core.Statements;

/// <summary>
/// The account-statement and report download order types the emulator generates content for (issue #40).
/// These are the classical order-type codes; the H005 <c>BTD</c> business transaction format is resolved to
/// one of them by <see cref="EBICO.Core.Btf.BtfOrderTypeCatalog.ResolveDownloadOrderType"/> before
/// generation.
/// </summary>
public static class StatementOrderTypes
{
    /// <summary>Statement of account, SWIFT MT940 (<c>STA</c>).</summary>
    public const string StatementMt940 = "STA";

    /// <summary>Interim transaction report, SWIFT MT942 (<c>VMK</c>).</summary>
    public const string InterimReportMt942 = "VMK";

    /// <summary>Bank-to-customer statement, camt.053 (<c>C53</c>).</summary>
    public const string StatementCamt053 = "C53";

    /// <summary>Bank-to-customer account report, camt.052 (<c>C52</c>).</summary>
    public const string ReportCamt052 = "C52";

    /// <summary>Bank-to-customer debit/credit notification, camt.054 (<c>C54</c>).</summary>
    public const string NotificationCamt054 = "C54";

    /// <summary>Whether <paramref name="orderType"/> is one of the statement/report download order types.</summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for STA/VMK/C53/C52/C54; otherwise <see langword="false"/>.</returns>
    public static bool IsStatementOrderType(string? orderType)
        => orderType is StatementMt940 or InterimReportMt942 or StatementCamt053 or ReportCamt052 or NotificationCamt054;
}
