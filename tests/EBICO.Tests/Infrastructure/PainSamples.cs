using System.Globalization;
using System.Text;

namespace EBICO.Tests.Infrastructure;

/// <summary>
/// Builds minimal, structurally valid ISO 20022 <c>pain.001</c> (credit transfer) and <c>pain.008</c>
/// (direct debit) payloads for the payment-order tests (issue #39). The messages carry only the elements
/// the structural <c>SepaPaymentValidator</c> inspects — group header, payment information and
/// transactions with instructed amounts — with the group-level <c>NbOfTxs</c>/<c>CtrlSum</c> kept
/// consistent. Negative cases are produced by string-editing a valid sample. No proprietary fixtures.
/// </summary>
internal static class PainSamples
{
    /// <summary>Builds a valid <c>pain.001</c> credit-transfer message for the given instructed amounts.</summary>
    /// <param name="amounts">One instructed amount per transaction (at least one).</param>
    /// <param name="messageId">The <c>GrpHdr/MsgId</c>.</param>
    /// <param name="messageVersion">The pain.001 message-name id / namespace tail.</param>
    /// <returns>The pain.001 XML as a string.</returns>
    public static string CreditTransfer(
        decimal[] amounts,
        string messageId = "MSG-CCT-0001",
        string messageVersion = "pain.001.001.09")
    {
        var ns = "urn:iso:std:iso:20022:tech:xsd:" + messageVersion;
        var (count, sum) = Totals(amounts);

        var transactions = new StringBuilder();
        for (var i = 0; i < amounts.Length; i++)
        {
            transactions.Append(
                $"""
                    <CdtTrfTxInf>
                      <PmtId><EndToEndId>E2E-{i + 1}</EndToEndId></PmtId>
                      <Amt><InstdAmt Ccy="EUR">{Format(amounts[i])}</InstdAmt></Amt>
                      <Cdtr><Nm>Creditor {i + 1}</Nm></Cdtr>
                      <CdtrAcct><Id><IBAN>DE02120300000000202051</IBAN></Id></CdtrAcct>
                    </CdtTrfTxInf>
                """);
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="{ns}">
              <CstmrCdtTrfInitn>
                <GrpHdr>
                  <MsgId>{messageId}</MsgId>
                  <CreDtTm>2026-07-14T10:00:00</CreDtTm>
                  <NbOfTxs>{count}</NbOfTxs>
                  <CtrlSum>{Format(sum)}</CtrlSum>
                  <InitgPty><Nm>EBICO Test</Nm></InitgPty>
                </GrpHdr>
                <PmtInf>
                  <PmtInfId>PMT-1</PmtInfId>
                  <PmtMtd>TRF</PmtMtd>
                  <Dbtr><Nm>Debtor</Nm></Dbtr>
                  <DbtrAcct><Id><IBAN>DE89370400440532013000</IBAN></Id></DbtrAcct>
                  <DbtrAgt><FinInstnId><BICFI>COBADEFFXXX</BICFI></FinInstnId></DbtrAgt>
            {transactions}
                </PmtInf>
              </CstmrCdtTrfInitn>
            </Document>
            """;
    }

    /// <summary>Builds a valid <c>pain.008</c> direct-debit message for the given instructed amounts.</summary>
    /// <param name="amounts">One instructed amount per transaction (at least one).</param>
    /// <param name="messageId">The <c>GrpHdr/MsgId</c>.</param>
    /// <param name="messageVersion">The pain.008 message-name id / namespace tail.</param>
    /// <returns>The pain.008 XML as a string.</returns>
    public static string DirectDebit(
        decimal[] amounts,
        string messageId = "MSG-CDD-0001",
        string messageVersion = "pain.008.001.02")
    {
        var ns = "urn:iso:std:iso:20022:tech:xsd:" + messageVersion;
        var (count, sum) = Totals(amounts);

        var transactions = new StringBuilder();
        for (var i = 0; i < amounts.Length; i++)
        {
            transactions.Append(
                $"""
                    <DrctDbtTxInf>
                      <PmtId><EndToEndId>E2E-{i + 1}</EndToEndId></PmtId>
                      <InstdAmt Ccy="EUR">{Format(amounts[i])}</InstdAmt>
                      <Dbtr><Nm>Debtor {i + 1}</Nm></Dbtr>
                      <DbtrAcct><Id><IBAN>DE89370400440532013000</IBAN></Id></DbtrAcct>
                    </DrctDbtTxInf>
                """);
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="{ns}">
              <CstmrDrctDbtInitn>
                <GrpHdr>
                  <MsgId>{messageId}</MsgId>
                  <CreDtTm>2026-07-14T10:00:00</CreDtTm>
                  <NbOfTxs>{count}</NbOfTxs>
                  <CtrlSum>{Format(sum)}</CtrlSum>
                  <InitgPty><Nm>EBICO Test</Nm></InitgPty>
                </GrpHdr>
                <PmtInf>
                  <PmtInfId>PMT-1</PmtInfId>
                  <PmtMtd>DD</PmtMtd>
                  <Cdtr><Nm>Creditor</Nm></Cdtr>
                  <CdtrAcct><Id><IBAN>DE02120300000000202051</IBAN></Id></CdtrAcct>
                  <CdtrAgt><FinInstnId><BICFI>COBADEFFXXX</BICFI></FinInstnId></CdtrAgt>
            {transactions}
                </PmtInf>
              </CstmrDrctDbtInitn>
            </Document>
            """;
    }

    private static (int Count, decimal Sum) Totals(decimal[] amounts)
    {
        var sum = 0m;
        foreach (var amount in amounts)
        {
            sum += amount;
        }

        return (amounts.Length, sum);
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
