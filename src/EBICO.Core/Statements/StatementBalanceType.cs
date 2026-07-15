namespace EBICO.Core.Statements;

/// <summary>
/// The role of a <see cref="StatementBalance"/> within a statement. The concrete ISO/SWIFT code is chosen
/// per format: camt.053 emits <c>OPBD</c>/<c>CLBD</c>, the camt.052 intraday report emits the interim code
/// <c>ITBD</c>, and MT940 emits <c>:60F:</c>/<c>:62F:</c>.
/// </summary>
public enum StatementBalanceType
{
    /// <summary>The balance carried forward at the start of the period (camt <c>OPBD</c>, MT <c>:60F:</c>).</summary>
    Opening,

    /// <summary>The balance at the end of the period (camt <c>CLBD</c>, MT <c>:62F:</c>).</summary>
    Closing,
}
