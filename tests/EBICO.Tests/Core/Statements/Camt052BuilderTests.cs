using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>Tests for <see cref="Camt052Builder"/> (C52): namespace, report root, interim (ITBD) balance, entries.</summary>
public class Camt052BuilderTests
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:camt.052.001.08";

    [Fact]
    public void Build_ProducesCamt052_WithInterimBalance()
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(Camt052Builder.Build(StatementSampleData.WithTwoEntries())));

        doc.Root!.Name.Should().Be(Ns + "Document");
        doc.Descendants(Ns + "BkToCstmrAcctRpt").Should().ContainSingle();
        doc.Descendants(Ns + "Bal")
            .Select(b => b.Element(Ns + "Tp")!.Element(Ns + "CdOrPrtry")!.Element(Ns + "Cd")!.Value)
            .Should().Contain("ITBD");
        doc.Descendants(Ns + "Ntry").Should().HaveCount(2);
    }
}
