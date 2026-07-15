namespace EBICO.Core.Statements;

/// <summary>
/// The direction of a booking or the side of a balance on an account statement. Rendered as ISO 20022
/// <c>CRDT</c>/<c>DBIT</c> (camt) and SWIFT <c>C</c>/<c>D</c> (MT940/MT942).
/// </summary>
public enum CreditDebitIndicator
{
    /// <summary>A credit (incoming) movement, or a credit-side balance (camt <c>CRDT</c>, MT <c>C</c>).</summary>
    Credit,

    /// <summary>A debit (outgoing) movement, or a debit-side balance (camt <c>DBIT</c>, MT <c>D</c>).</summary>
    Debit,
}
