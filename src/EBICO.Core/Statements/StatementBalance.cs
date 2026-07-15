namespace EBICO.Core.Statements;

/// <summary>
/// An opening or closing balance on an account statement. The <see cref="Amount"/> is the unsigned
/// magnitude; <see cref="CreditDebit"/> carries the side (a debit balance is an overdraft), matching how
/// both camt (<c>CdtDbtInd</c>) and MT940 (<c>:60F:</c>/<c>:62F:</c>) mark balances independently of the
/// number's sign.
/// </summary>
/// <param name="Type">Whether this is the opening or the closing balance.</param>
/// <param name="Amount">The unsigned balance magnitude.</param>
/// <param name="CreditDebit">The balance side (credit or debit).</param>
/// <param name="Date">The value date the balance applies to.</param>
public readonly record struct StatementBalance(
    StatementBalanceType Type,
    decimal Amount,
    CreditDebitIndicator CreditDebit,
    DateOnly Date);
