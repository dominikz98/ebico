using System.Globalization;
using System.Xml.Linq;
using EBICO.Core.Payments;

namespace EBICO.Core.Statements;

/// <summary>
/// Builds a minimal, well-formed ISO 20022 <b>camt.054.001.08</b> Bank-to-Customer Debit/Credit
/// Notification (order type <see cref="OrderType"/>, issue #40) from an <see cref="AccountStatement"/>: a
/// single <c>Ntfctn</c> with the account and one booked <c>Ntry</c> per entry. A notification reports
/// movements only, so it carries <b>no</b> balances. Deterministic UTF-8 XML (no BOM).
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the message version is fixed to <c>camt.054.001.08</c>; not validated against
/// the proprietary XSD.
/// </remarks>
public static class Camt054Builder
{
    /// <summary>The classical download order type this builder produces (<c>C54</c>).</summary>
    public const string OrderType = StatementOrderTypes.NotificationCamt054;

    /// <summary>The ISO 20022 message-name id / namespace tail (<c>camt.054.001.08</c>).</summary>
    public const string MessageNameId = "camt.054.001.08";

    /// <summary>Builds the camt.054 notification as deterministic UTF-8 XML bytes (no BOM).</summary>
    /// <param name="statement">The statement to render.</param>
    /// <returns>The camt.054 document as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="statement"/> is <see langword="null"/>.</exception>
    public static byte[] Build(AccountStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        XNamespace ns = SepaPaymentValidator.IsoNamespacePrefix + MessageNameId;
        var currency = statement.Account.Currency;

        var ntfctn = new XElement(
            ns + "Ntfctn",
            new XElement(ns + "Id", statement.StatementId),
            new XElement(ns + "ElctrncSeqNb", statement.ElectronicSequenceNumber.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "CreDtTm", CamtXml.DateTimeText(statement.CreationTimestamp)),
            CamtXml.FromToDate(ns, statement.RangeStart, statement.RangeEnd),
            CamtXml.Account(ns, statement.Account));

        foreach (var entry in statement.Entries)
        {
            ntfctn.Add(CamtXml.Entry(ns, entry, currency));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                ns + "Document",
                new XElement(
                    ns + "BkToCstmrDbtCdtNtfctn",
                    CamtXml.GroupHeader(ns, statement.StatementId, statement.CreationTimestamp),
                    ntfctn)));

        return CamtXml.Serialize(document);
    }
}
