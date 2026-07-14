using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core.Payments;

namespace EBICO.Tests.Core.Payments;

/// <summary>
/// Tests for the <see cref="PainStatusReportBuilder"/> (issue #39): the produced <c>pain.002</c> echoes
/// the original message identifiers, carries the group status and is well-formed XML in the pain.002
/// namespace.
/// </summary>
public class PainStatusReportBuilderTests
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";

    [Fact]
    public void Build_EchoesOriginalIdentifiersAndStatus()
    {
        var creation = new DateTimeOffset(2026, 7, 14, 8, 30, 0, TimeSpan.Zero);

        var bytes = PainStatusReportBuilder.Build(
            originalMessageId: "MSG-CCT-0001",
            originalMessageNameId: "pain.001.001.09",
            reportMessageId: "PSR-abc123",
            creationDateTime: creation);

        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        var report = document.Root!.Element(Ns + "CstmrPmtStsRpt")!;

        report.Element(Ns + "GrpHdr")!.Element(Ns + "MsgId")!.Value.Should().Be("PSR-abc123");
        report.Element(Ns + "GrpHdr")!.Element(Ns + "CreDtTm")!.Value.Should().Be("2026-07-14T08:30:00Z");

        var status = report.Element(Ns + "OrgnlGrpInfAndSts")!;
        status.Element(Ns + "OrgnlMsgId")!.Value.Should().Be("MSG-CCT-0001");
        status.Element(Ns + "OrgnlMsgNmId")!.Value.Should().Be("pain.001.001.09");
        status.Element(Ns + "GrpSts")!.Value.Should().Be(PainStatusReportBuilder.AcceptedGroupStatus);
    }

    [Fact]
    public void Build_ProducesWellFormedDocumentRoot()
    {
        var bytes = PainStatusReportBuilder.Build("M1", "pain.008.001.02", "PSR-1", DateTimeOffset.UnixEpoch);

        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));

        document.Root!.Name.LocalName.Should().Be("Document");
        document.Root.Name.NamespaceName.Should().Be(Ns.NamespaceName);
    }

    [Theory]
    [InlineData("", "pain.001.001.09", "PSR-1")]
    [InlineData("M1", " ", "PSR-1")]
    [InlineData("M1", "pain.001.001.09", "")]
    public void Build_BlankIdentifier_Throws(string originalMessageId, string originalMessageNameId, string reportMessageId)
    {
        var act = () => PainStatusReportBuilder.Build(originalMessageId, originalMessageNameId, reportMessageId, DateTimeOffset.UnixEpoch);

        act.Should().Throw<ArgumentException>();
    }
}
