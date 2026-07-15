namespace EBICO.Core.Statements;

/// <summary>
/// A complete account statement for one account over one reporting period: the account, the period bounds,
/// the opening and closing balances, the ordered booking entries and the statement identity (numbers +
/// creation timestamp). It is the single, format-neutral source every statement builder
/// (MT940/MT942/camt.05x) renders, so all formats emit the same numbers.
/// </summary>
/// <remarks>
/// Invariant (established by the producer, relied on by every builder):
/// <c>ClosingBalance = OpeningBalance ± Σ(entries)</c>, where a credit entry increases and a debit entry
/// decreases a credit-side balance. The camt.052 intraday report reuses the balances as interim
/// (<c>ITBD</c>) values; the camt.054 notification ignores the balances entirely.
/// </remarks>
/// <param name="Account">The account the statement is drawn for.</param>
/// <param name="RangeStart">The inclusive first day of the reporting period.</param>
/// <param name="RangeEnd">The inclusive last day of the reporting period.</param>
/// <param name="OpeningBalance">The balance carried forward at <paramref name="RangeStart"/>.</param>
/// <param name="ClosingBalance">The balance at <paramref name="RangeEnd"/>.</param>
/// <param name="Entries">The ordered booking entries within the period (possibly empty).</param>
/// <param name="StatementId">The statement reference (MT940 <c>:20:</c> / camt <c>Id</c>).</param>
/// <param name="StatementNumber">The statement number (MT940 <c>:28C:</c> statement number).</param>
/// <param name="SequenceNumber">The page/sequence number within the statement (MT940 <c>:28C:</c> sequence).</param>
/// <param name="ElectronicSequenceNumber">The electronic sequence number (camt <c>ElctrncSeqNb</c>).</param>
/// <param name="CreationTimestamp">The statement creation time (camt <c>CreDtTm</c>; also the ZIP entry timestamp).</param>
public sealed record AccountStatement(
    StatementAccount Account,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    StatementBalance OpeningBalance,
    StatementBalance ClosingBalance,
    IReadOnlyList<StatementEntry> Entries,
    string StatementId,
    int StatementNumber,
    int SequenceNumber,
    long ElectronicSequenceNumber,
    DateTimeOffset CreationTimestamp);
