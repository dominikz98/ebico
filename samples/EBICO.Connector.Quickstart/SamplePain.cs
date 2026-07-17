using System.Globalization;
using System.Text;

namespace EBICO.Connector.Quickstart;

/// <summary>
/// Builds a minimal, structurally valid ISO 20022 <c>pain.001</c> (SEPA credit transfer) message for the
/// quickstart upload. It carries exactly the elements the server's structural <c>SepaPaymentValidator</c>
/// inspects (group header, one payment-information block, transactions with instructed amounts) with a
/// consistent <c>NbOfTxs</c>/<c>CtrlSum</c>. Deliberately self-authored, non-proprietary sample data — no
/// EBICS/ISO fixtures are shipped.
/// </summary>
internal static class SamplePain
{
    /// <summary>Builds a valid <c>pain.001</c> credit-transfer message for the given instructed amounts.</summary>
    /// <param name="amounts">One instructed amount per transaction (at least one).</param>
    /// <returns>The pain.001 XML as a string.</returns>
    public static string CreditTransfer(params decimal[] amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);
        if (amounts.Length == 0)
        {
            throw new ArgumentException("At least one amount is required.", nameof(amounts));
        }

        const string ns = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

        var sum = 0m;
        var transactions = new StringBuilder();
        for (var i = 0; i < amounts.Length; i++)
        {
            sum += amounts[i];
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
                  <MsgId>MSG-CCT-QUICKSTART</MsgId>
                  <CreDtTm>2026-07-14T10:00:00</CreDtTm>
                  <NbOfTxs>{amounts.Length}</NbOfTxs>
                  <CtrlSum>{Format(sum)}</CtrlSum>
                  <InitgPty><Nm>EBICO Quickstart</Nm></InitgPty>
                </GrpHdr>
                <PmtInf>
                  <PmtInfId>PMT-1</PmtInfId>
                  <PmtMtd>TRF</PmtMtd>
                  <Dbtr><Nm>Debtor</Nm></Dbtr>
                  <DbtrAcct><Id><IBAN>DE89370400440532013000</IBAN></Id></DbtrAcct>
                  <DbtrAgt><FinInstnId><BICFI>COBADEFFXXX</BICFI></FinInstnId></DbtrAgt>{transactions}
                </PmtInf>
              </CstmrCdtTrfInitn>
            </Document>
            """;
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
