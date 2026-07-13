namespace EBICO.Server.Transactions;

/// <summary>
/// The version-neutral EBICS transaction phase. Mirrors the three per-version
/// <c>TransactionPhaseType</c> bindings (<c>Initialisation</c>/<c>Transfer</c>/<c>Receipt</c>) so the
/// transaction engine and the response factory can reason about the phase without switching on the
/// protocol version.
/// </summary>
public enum EbicsTransactionPhase
{
    /// <summary>The initialisation phase: the server assigns the transaction id and stores its state.</summary>
    Initialisation,

    /// <summary>The transfer phase: one order-data segment is exchanged per message.</summary>
    Transfer,

    /// <summary>The receipt phase: the client acknowledges a completed download (not used for uploads).</summary>
    Receipt,
}
