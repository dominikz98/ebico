using System.Globalization;
using System.Xml.Linq;
using EBICO.Core.Payments;

namespace EBICO.Core.Statements;

/// <summary>
/// Builds a minimal, well-formed ISO 20022 <b>camt.053.001.08</b> Bank-to-Customer Statement (order type
/// <see cref="OrderType"/>, issue #40) from an <see cref="AccountStatement"/>: a single <c>Stmt</c> with the
/// account, the opening (<c>OPBD</c>) and closing (<c>CLBD</c>) balances and one booked <c>Ntry</c> per
/// entry. Deterministic UTF-8 XML (no BOM), mirroring <see cref="PainStatusReportBuilder"/>.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the message version is fixed to <c>camt.053.001.08</c> (the modern ISO/CGI-MP
/// profile; the classic Deutsche-Kreditwirtschaft profile is <c>.02</c> and uses a plain <c>&lt;Sts&gt;BOOK&lt;/Sts&gt;</c>
/// instead of the structured <c>&lt;Sts&gt;&lt;Cd&gt;BOOK&lt;/Cd&gt;&lt;/Sts&gt;</c> emitted here). Not validated against the proprietary XSD.
/// </remarks>
public static class Camt053Builder
{
    /// <summary>The classical download order type this builder produces (<c>C53</c>).</summary>
    public const string OrderType = StatementOrderTypes.StatementCamt053;

    /// <summary>The ISO 20022 message-name id / namespace tail (<c>camt.053.001.08</c>).</summary>
    public const string MessageNameId = "camt.053.001.08";

    /// <summary>Builds the camt.053 statement as deterministic UTF-8 XML bytes (no BOM).</summary>
    /// <param name="statement">The statement to render.</param>
    /// <returns>The camt.053 document as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="statement"/> is <see langword="null"/>.</exception>
    public static byte[] Build(AccountStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        XNamespace ns = SepaPaymentValidator.IsoNamespacePrefix + MessageNameId;
        var currency = statement.Account.Currency;

        var stmt = new XElement(
            ns + "Stmt",
            new XElement(ns + "Id", statement.StatementId),
            new XElement(ns + "ElctrncSeqNb", statement.ElectronicSequenceNumber.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "CreDtTm", CamtXml.DateTimeText(statement.CreationTimestamp)),
            CamtXml.FromToDate(ns, statement.RangeStart, statement.RangeEnd),
            CamtXml.Account(ns, statement.Account),
            CamtXml.Balance(ns, "OPBD", statement.OpeningBalance, currency),
            CamtXml.Balance(ns, "CLBD", statement.ClosingBalance, currency));

        foreach (var entry in statement.Entries)
        {
            stmt.Add(CamtXml.Entry(ns, entry, currency));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                ns + "Document",
                new XElement(
                    ns + "BkToCstmrStmt",
                    CamtXml.GroupHeader(ns, statement.StatementId, statement.CreationTimestamp),
                    stmt)));

        return CamtXml.Serialize(document);
    }
}
