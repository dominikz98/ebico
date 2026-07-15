using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EBICO.Core.Statements;

/// <summary>
/// Shared ISO 20022 camt (camt.052/053/054) building blocks: the hardened serializer, the ISO date/amount
/// formatting and the common <c>GrpHdr</c>/<c>Acct</c>/<c>Bal</c>/<c>Ntry</c> sub-trees. Centralised so the
/// three camt builders emit structurally identical accounts, balances and entries and only differ in the
/// namespace and the statement/report/notification level element.
/// </summary>
internal static class CamtXml
{
    // Mirrors PainStatusReportBuilder: deterministic UTF-8 (no BOM), no indentation, keep the declaration.
    internal static byte[] Serialize(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
        };

        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }

        return buffer.ToArray();
    }

    internal static string DateTimeText(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    internal static string DateText(DateOnly value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string AmountText(decimal value)
        => value.ToString("F2", CultureInfo.InvariantCulture);

    internal static string CdtDbt(CreditDebitIndicator indicator)
        => indicator == CreditDebitIndicator.Credit ? "CRDT" : "DBIT";

    internal static XElement GroupHeader(XNamespace ns, string messageId, DateTimeOffset creation)
        => new(
            ns + "GrpHdr",
            new XElement(ns + "MsgId", messageId),
            new XElement(ns + "CreDtTm", DateTimeText(creation)));

    internal static XElement FromToDate(XNamespace ns, DateOnly rangeStart, DateOnly rangeEnd)
        => new(
            ns + "FrToDt",
            new XElement(ns + "FrDtTm", DateTimeText(new DateTimeOffset(rangeStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))),
            new XElement(ns + "ToDtTm", DateTimeText(new DateTimeOffset(rangeEnd.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero))));

    internal static XElement Account(XNamespace ns, StatementAccount account)
        => new(
            ns + "Acct",
            new XElement(ns + "Id", new XElement(ns + "IBAN", account.Iban)),
            new XElement(ns + "Ccy", account.Currency),
            new XElement(ns + "Ownr", new XElement(ns + "Nm", account.OwnerName)),
            new XElement(
                ns + "Svcr",
                new XElement(ns + "FinInstnId", new XElement(ns + "BICFI", account.Bic))));

    internal static XElement Balance(XNamespace ns, string code, StatementBalance balance, string currency)
        => new(
            ns + "Bal",
            new XElement(ns + "Tp", new XElement(ns + "CdOrPrtry", new XElement(ns + "Cd", code))),
            new XElement(ns + "Amt", new XAttribute("Ccy", currency), AmountText(balance.Amount)),
            new XElement(ns + "CdtDbtInd", CdtDbt(balance.CreditDebit)),
            new XElement(ns + "Dt", new XElement(ns + "Dt", DateText(balance.Date))));

    internal static XElement Entry(XNamespace ns, StatementEntry entry, string currency)
    {
        // The counterparty sits on the opposite side of the movement: an incoming (credit) entry names the
        // debtor, an outgoing (debit) entry names the creditor.
        var isCredit = entry.CreditDebit == CreditDebitIndicator.Credit;
        var partyElement = isCredit ? "Dbtr" : "Cdtr";
        var partyAccountElement = isCredit ? "DbtrAcct" : "CdtrAcct";

        return new XElement(
            ns + "Ntry",
            new XElement(ns + "Amt", new XAttribute("Ccy", currency), AmountText(entry.Amount)),
            new XElement(ns + "CdtDbtInd", CdtDbt(entry.CreditDebit)),
            new XElement(ns + "Sts", new XElement(ns + "Cd", "BOOK")),
            new XElement(ns + "BookgDt", new XElement(ns + "Dt", DateText(entry.BookingDate))),
            new XElement(ns + "ValDt", new XElement(ns + "Dt", DateText(entry.ValueDate))),
            new XElement(
                ns + "NtryDtls",
                new XElement(
                    ns + "TxDtls",
                    new XElement(ns + "Refs", new XElement(ns + "EndToEndId", entry.EndToEndId)),
                    new XElement(ns + "RmtInf", new XElement(ns + "Ustrd", entry.RemittanceInfo)),
                    new XElement(
                        ns + "RltdPties",
                        new XElement(ns + partyElement, new XElement(ns + "Nm", entry.CounterpartyName)),
                        new XElement(
                            ns + partyAccountElement,
                            new XElement(ns + "Id", new XElement(ns + "IBAN", entry.CounterpartyIban)))))));
    }
}
