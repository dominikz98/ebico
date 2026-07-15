using System.Globalization;
using System.Xml.Linq;
using EBICO.Core.Payments;

namespace EBICO.Core.Statements;

/// <summary>
/// Builds a minimal, well-formed ISO 20022 <b>camt.052.001.08</b> Bank-to-Customer Account Report (order
/// type <see cref="OrderType"/>, issue #40) from an <see cref="AccountStatement"/>: a single <c>Rpt</c>
/// (intraday report) with the account, one interim booked balance (<c>ITBD</c>) and one booked <c>Ntry</c>
/// per entry. Deterministic UTF-8 XML (no BOM).
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the message version is fixed to <c>camt.052.001.08</c>; a report is intraday,
/// so it carries an interim balance rather than the booked <c>OPBD</c>/<c>CLBD</c> pair of camt.053. Not
/// validated against the proprietary XSD.
/// </remarks>
public static class Camt052Builder
{
    /// <summary>The classical download order type this builder produces (<c>C52</c>).</summary>
    public const string OrderType = StatementOrderTypes.ReportCamt052;

    /// <summary>The ISO 20022 message-name id / namespace tail (<c>camt.052.001.08</c>).</summary>
    public const string MessageNameId = "camt.052.001.08";

    /// <summary>Builds the camt.052 account report as deterministic UTF-8 XML bytes (no BOM).</summary>
    /// <param name="statement">The statement to render.</param>
    /// <returns>The camt.052 document as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="statement"/> is <see langword="null"/>.</exception>
    public static byte[] Build(AccountStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        XNamespace ns = SepaPaymentValidator.IsoNamespacePrefix + MessageNameId;
        var currency = statement.Account.Currency;

        var rpt = new XElement(
            ns + "Rpt",
            new XElement(ns + "Id", statement.StatementId),
            new XElement(ns + "ElctrncSeqNb", statement.ElectronicSequenceNumber.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "CreDtTm", CamtXml.DateTimeText(statement.CreationTimestamp)),
            CamtXml.FromToDate(ns, statement.RangeStart, statement.RangeEnd),
            CamtXml.Account(ns, statement.Account),
            CamtXml.Balance(ns, "ITBD", statement.ClosingBalance, currency));

        foreach (var entry in statement.Entries)
        {
            rpt.Add(CamtXml.Entry(ns, entry, currency));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                ns + "Document",
                new XElement(
                    ns + "BkToCstmrAcctRpt",
                    CamtXml.GroupHeader(ns, statement.StatementId, statement.CreationTimestamp),
                    rpt)));

        return CamtXml.Serialize(document);
    }
}
