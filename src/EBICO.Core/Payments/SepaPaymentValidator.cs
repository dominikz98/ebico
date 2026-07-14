using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EBICO.Core.Payments;

/// <summary>
/// Validates an uploaded SEPA <c>pain</c> payload (<c>pain.001</c> for CCT/CIP, <c>pain.008</c> for
/// CDD/CDB) <b>structurally and semantically</b> — <em>not</em> against the full ISO 20022 XSD (which is
/// not shipped with the repository, ADR-0017). The checks cover: well-formed XML, the <c>Document</c>
/// root in the expected ISO namespace family, the initiation root element, the mandatory group header
/// fields (<c>MsgId</c>/<c>CreDtTm</c>/<c>NbOfTxs</c>), at least one payment-information block and one
/// transaction, and the two cross-checks the format guarantees: <c>NbOfTxs</c> equals the actual
/// transaction count and — when present — <c>CtrlSum</c> equals the sum of the instructed amounts.
/// </summary>
/// <remarks>
/// The validation is deliberately namespace-version tolerant: elements are matched by local name so any
/// <c>pain.001.001.xx</c> / <c>pain.008.001.xx</c> revision is accepted, as long as the document
/// namespace belongs to the expected family. Parsing is hardened against DTD/XXE (no
/// <see cref="XmlResolver"/>, <see cref="DtdProcessing.Prohibit"/>), consistent with
/// <see cref="EBICO.Core.Serialization.EbicsXmlSerializer"/>.
/// </remarks>
public static class SepaPaymentValidator
{
    /// <summary>The common ISO 20022 message namespace prefix (<c>urn:iso:std:iso:20022:tech:xsd:</c>).</summary>
    public const string IsoNamespacePrefix = "urn:iso:std:iso:20022:tech:xsd:";

    /// <summary>
    /// Validates <paramref name="payload"/> as the SEPA pain message expected for
    /// <paramref name="orderType"/>.
    /// </summary>
    /// <param name="orderType">The resolved payment order type (CCT/CIP/CDD/CDB).</param>
    /// <param name="payload">The decoded (decrypted/decompressed) order-data bytes: the UTF-8 pain XML.</param>
    /// <returns>A <see cref="PainValidationResult"/> describing success (with identifiers) or the errors.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    public static PainValidationResult Validate(string? orderType, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!PaymentOrderTypes.TryGetExpectedMessage(orderType, out var messageFamily, out var initiationRootLocalName))
        {
            return PainValidationResult.Invalid([$"Order type '{orderType}' is not a supported SEPA payment order type."]);
        }

        XDocument document;
        try
        {
            document = Parse(payload);
        }
        catch (Exception ex) when (ex is XmlException or FormatException)
        {
            return PainValidationResult.Invalid([$"The order data is not well-formed XML: {ex.Message}"]);
        }

        var errors = new List<string>();

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Document")
        {
            return PainValidationResult.Invalid([$"Expected a <Document> root element but found '{root?.Name.LocalName ?? "(none)"}'."]);
        }

        var documentNamespace = root.Name.NamespaceName;
        // Match the family at a version boundary ("pain.001" or "pain.001.001.09"), not a bare prefix,
        // so a hypothetical "pain.0019…" is not mistaken for the "pain.001" family.
        var expectedNamespaceRoot = IsoNamespacePrefix + messageFamily;
        var isExpectedFamily = documentNamespace.Equals(expectedNamespaceRoot, StringComparison.Ordinal)
            || documentNamespace.StartsWith(expectedNamespaceRoot + ".", StringComparison.Ordinal);
        if (!isExpectedFamily)
        {
            errors.Add($"The document namespace '{documentNamespace}' does not belong to the expected '{messageFamily}' family.");
        }

        var initiation = root.Elements().FirstOrDefault(e => e.Name.LocalName == initiationRootLocalName);
        if (initiation is null)
        {
            errors.Add($"The <Document> is missing the expected <{initiationRootLocalName}> element.");
            return PainValidationResult.Invalid(errors);
        }

        var groupHeader = initiation.Elements().FirstOrDefault(e => e.Name.LocalName == "GrpHdr");
        var messageId = groupHeader is null ? null : ChildValue(groupHeader, "MsgId");
        if (groupHeader is null)
        {
            errors.Add("The initiation is missing the <GrpHdr> element.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                errors.Add("The <GrpHdr> is missing a non-empty <MsgId>.");
            }

            if (string.IsNullOrWhiteSpace(ChildValue(groupHeader, "CreDtTm")))
            {
                errors.Add("The <GrpHdr> is missing a non-empty <CreDtTm>.");
            }
        }

        var paymentInfos = initiation.Elements().Where(e => e.Name.LocalName == "PmtInf").ToList();
        if (paymentInfos.Count == 0)
        {
            errors.Add("The initiation contains no <PmtInf> payment-information block.");
        }

        var transactionLocalName = messageFamily == PaymentOrderTypes.CreditTransferMessageFamily
            ? "CdtTrfTxInf"
            : "DrctDbtTxInf";
        var transactions = paymentInfos
            .SelectMany(p => p.Elements().Where(e => e.Name.LocalName == transactionLocalName))
            .ToList();
        if (transactions.Count == 0)
        {
            errors.Add($"The initiation contains no <{transactionLocalName}> transaction.");
        }

        // Cross-check 1: NbOfTxs equals the actual number of transactions.
        var declaredCount = groupHeader is null ? null : ChildValue(groupHeader, "NbOfTxs");
        if (string.IsNullOrWhiteSpace(declaredCount))
        {
            errors.Add("The <GrpHdr> is missing a non-empty <NbOfTxs>.");
        }
        else if (!long.TryParse(declaredCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nbOfTxs))
        {
            errors.Add($"The <NbOfTxs> value '{declaredCount}' is not a valid integer.");
        }
        else if (nbOfTxs != transactions.Count)
        {
            errors.Add($"The <NbOfTxs> value {nbOfTxs} does not match the {transactions.Count} transaction(s) present.");
        }

        // Cross-check 2: CtrlSum (when present) equals the sum of the instructed amounts.
        var controlSum = groupHeader is null ? null : ChildValue(groupHeader, "CtrlSum");
        if (!string.IsNullOrWhiteSpace(controlSum))
        {
            if (!decimal.TryParse(controlSum, NumberStyles.Number, CultureInfo.InvariantCulture, out var declaredSum))
            {
                errors.Add($"The <CtrlSum> value '{controlSum}' is not a valid decimal.");
            }
            else
            {
                var actualSum = 0m;
                var amountParseFailed = false;
                foreach (var transaction in transactions)
                {
                    var amount = transaction.Descendants().FirstOrDefault(e => e.Name.LocalName == "InstdAmt");
                    if (amount is null || !decimal.TryParse(amount.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                    {
                        amountParseFailed = true;
                        break;
                    }

                    actualSum += value;
                }

                if (amountParseFailed)
                {
                    errors.Add("A transaction is missing a valid <InstdAmt> instructed amount.");
                }
                else if (declaredSum != actualSum)
                {
                    errors.Add($"The <CtrlSum> value {declaredSum.ToString(CultureInfo.InvariantCulture)} does not match the sum {actualSum.ToString(CultureInfo.InvariantCulture)} of the instructed amounts.");
                }
            }
        }

        if (errors.Count > 0)
        {
            return PainValidationResult.Invalid(errors);
        }

        // messageId is guaranteed non-null/blank here (validated above); the message-name id is the
        // namespace tail (e.g. "pain.001.001.09").
        var messageNameId = documentNamespace.StartsWith(IsoNamespacePrefix, StringComparison.Ordinal)
            ? documentNamespace[IsoNamespacePrefix.Length..]
            : documentNamespace;
        return PainValidationResult.Valid(messageId!, messageNameId);
    }

    private static XDocument Parse(byte[] payload)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using var stream = new MemoryStream(payload, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    private static string? ChildValue(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
}
