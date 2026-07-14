namespace EBICO.Core.Payments;

/// <summary>
/// The SEPA payment upload order types processed by the emulator (issue #39) and their mapping to the
/// expected ISO 20022 <c>pain</c> message family. These are the classical order-type codes; the H005
/// <c>BTU</c> business transaction format is resolved to one of them by
/// <see cref="EBICO.Core.Btf.BtfOrderTypeCatalog.ResolveUploadOrderType"/> before processing.
/// </summary>
public static class PaymentOrderTypes
{
    /// <summary>SEPA Credit Transfer (<c>pain.001</c>).</summary>
    public const string CreditTransfer = "CCT";

    /// <summary>SEPA Instant Credit Transfer (<c>pain.001</c>, service option <c>INST</c>).</summary>
    public const string InstantCreditTransfer = "CIP";

    /// <summary>SEPA Direct Debit, CORE scheme (<c>pain.008</c>).</summary>
    public const string DirectDebitCore = "CDD";

    /// <summary>SEPA Direct Debit, B2B scheme (<c>pain.008</c>).</summary>
    public const string DirectDebitB2B = "CDB";

    /// <summary>The ISO 20022 credit-transfer message family (<c>pain.001</c>).</summary>
    public const string CreditTransferMessageFamily = "pain.001";

    /// <summary>The ISO 20022 direct-debit message family (<c>pain.008</c>).</summary>
    public const string DirectDebitMessageFamily = "pain.008";

    /// <summary>Whether <paramref name="orderType"/> is one of the SEPA payment upload order types.</summary>
    /// <param name="orderType">The (already resolved) classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for CCT/CIP/CDD/CDB; otherwise <see langword="false"/>.</returns>
    public static bool IsPaymentOrderType(string? orderType)
        => orderType is CreditTransfer or InstantCreditTransfer or DirectDebitCore or DirectDebitB2B;

    /// <summary>
    /// Resolves the ISO 20022 message family and the expected <c>Document</c> child (initiation root)
    /// local name for a payment order type.
    /// </summary>
    /// <param name="orderType">The classical order-type code (CCT/CIP/CDD/CDB), or <see langword="null"/>.</param>
    /// <param name="messageFamily">The expected pain message family (e.g. <c>"pain.001"</c>) when the method returns <see langword="true"/>.</param>
    /// <param name="initiationRootLocalName">The expected initiation root element local name (e.g. <c>"CstmrCdtTrfInitn"</c>) when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> for a known payment order type; otherwise <see langword="false"/>.</returns>
    public static bool TryGetExpectedMessage(string? orderType, out string messageFamily, out string initiationRootLocalName)
    {
        switch (orderType)
        {
            case CreditTransfer or InstantCreditTransfer:
                messageFamily = CreditTransferMessageFamily;
                initiationRootLocalName = "CstmrCdtTrfInitn";
                return true;
            case DirectDebitCore or DirectDebitB2B:
                messageFamily = DirectDebitMessageFamily;
                initiationRootLocalName = "CstmrDrctDbtInitn";
                return true;
            default:
                messageFamily = string.Empty;
                initiationRootLocalName = string.Empty;
                return false;
        }
    }
}
