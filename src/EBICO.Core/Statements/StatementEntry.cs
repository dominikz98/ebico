namespace EBICO.Core.Statements;

/// <summary>
/// A single booking on an account statement. The <see cref="Amount"/> is always the unsigned magnitude;
/// the direction is in <see cref="CreditDebit"/>.
/// </summary>
/// <param name="BookingDate">The date the entry was booked.</param>
/// <param name="ValueDate">The value date the entry takes effect for.</param>
/// <param name="Amount">The unsigned booking magnitude.</param>
/// <param name="CreditDebit">The booking direction (credit = incoming, debit = outgoing).</param>
/// <param name="RemittanceInfo">The unstructured remittance information (purpose text).</param>
/// <param name="CounterpartyName">The counterparty's name.</param>
/// <param name="CounterpartyIban">The counterparty's IBAN.</param>
/// <param name="EndToEndId">The end-to-end identifier of the underlying payment.</param>
/// <param name="Reference">The bank reference of the entry (used as the MT940 <c>:61:</c> reference).</param>
public readonly record struct StatementEntry(
    DateOnly BookingDate,
    DateOnly ValueDate,
    decimal Amount,
    CreditDebitIndicator CreditDebit,
    string RemittanceInfo,
    string CounterpartyName,
    string CounterpartyIban,
    string EndToEndId,
    string Reference);
