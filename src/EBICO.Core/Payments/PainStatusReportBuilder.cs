using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EBICO.Core.Payments;

/// <summary>
/// Builds a minimal, structurally plausible ISO 20022 <c>pain.002</c> Customer Payment Status Report for
/// an accepted (or rejected) payment upload (issue #39). The report echoes the original message
/// identifiers (<c>OrgnlMsgId</c>/<c>OrgnlMsgNmId</c>) and a group-level status; it is filed for later
/// download (the "Ablage zur späteren Auslieferung"). This is intentionally a <em>group-level</em>
/// acknowledgement — per-transaction status blocks are out of scope (ADR-0017).
/// </summary>
public static class PainStatusReportBuilder
{
    /// <summary>The ISO message-name id of the produced status report (<c>pain.002.001.03</c>).</summary>
    public const string DefaultStatusReportMessageNameId = "pain.002.001.03";

    /// <summary>Group status "Accepted Customer Profile" (<c>ACCP</c>) — a positive acknowledgement.</summary>
    public const string AcceptedGroupStatus = "ACCP";

    /// <summary>Group status "Rejected" (<c>RJCT</c>).</summary>
    public const string RejectedGroupStatus = "RJCT";

    /// <summary>
    /// Builds the <c>pain.002</c> status report as deterministic UTF-8 XML bytes (no BOM).
    /// </summary>
    /// <param name="originalMessageId">The uploaded message's <c>GrpHdr/MsgId</c> (echoed as <c>OrgnlMsgId</c>).</param>
    /// <param name="originalMessageNameId">The uploaded message's ISO message-name id, e.g. <c>"pain.001.001.09"</c> (echoed as <c>OrgnlMsgNmId</c>).</param>
    /// <param name="reportMessageId">The <c>MsgId</c> of the status report itself (server-assigned).</param>
    /// <param name="creationDateTime">The report creation timestamp (rendered as UTC <c>CreDtTm</c>).</param>
    /// <param name="groupStatus">The group status code (default <see cref="AcceptedGroupStatus"/>).</param>
    /// <param name="statusReportMessageNameId">The pain.002 message-name id / namespace tail (default <see cref="DefaultStatusReportMessageNameId"/>).</param>
    /// <returns>The serialized <c>pain.002</c> report as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentException">A required identifier is null, empty or whitespace.</exception>
    public static byte[] Build(
        string originalMessageId,
        string originalMessageNameId,
        string reportMessageId,
        DateTimeOffset creationDateTime,
        string groupStatus = AcceptedGroupStatus,
        string statusReportMessageNameId = DefaultStatusReportMessageNameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalMessageNameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupStatus);
        ArgumentException.ThrowIfNullOrWhiteSpace(statusReportMessageNameId);

        XNamespace ns = SepaPaymentValidator.IsoNamespacePrefix + statusReportMessageNameId;
        var creationText = creationDateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                ns + "Document",
                new XElement(
                    ns + "CstmrPmtStsRpt",
                    new XElement(
                        ns + "GrpHdr",
                        new XElement(ns + "MsgId", reportMessageId),
                        new XElement(ns + "CreDtTm", creationText)),
                    new XElement(
                        ns + "OrgnlGrpInfAndSts",
                        new XElement(ns + "OrgnlMsgId", originalMessageId),
                        new XElement(ns + "OrgnlMsgNmId", originalMessageNameId),
                        new XElement(ns + "GrpSts", groupStatus)))));

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
}
