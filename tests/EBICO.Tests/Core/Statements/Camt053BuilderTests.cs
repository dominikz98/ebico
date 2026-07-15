using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>Tests for <see cref="Camt053Builder"/> (C53): namespace, statement root, OPBD/CLBD balances, entries.</summary>
public class Camt053BuilderTests
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.08";

    [Fact]
    public void Build_ProducesCamt053_WithBalancesAndEntries()
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(Camt053Builder.Build(StatementSampleData.WithTwoEntries())));

        doc.Root!.Name.Should().Be(Ns + "Document");
        doc.Descendants(Ns + "BkToCstmrStmt").Should().ContainSingle();
        BalanceCodes(doc).Should().Contain(["OPBD", "CLBD"]);
        doc.Descendants(Ns + "Ntry").Should().HaveCount(2);
        doc.Descendants(Ns + "Ntry").First().Element(Ns + "Amt")!.Attribute("Ccy")!.Value.Should().Be("EUR");
        doc.Descendants(Ns + "Ntry").SelectMany(n => n.Descendants(Ns + "CdtDbtInd")).Select(e => e.Value)
            .Should().Contain(["CRDT", "DBIT"]);
    }

    private static IEnumerable<string> BalanceCodes(XDocument doc)
        => doc.Descendants(Ns + "Bal").Select(b => b.Element(Ns + "Tp")!.Element(Ns + "CdOrPrtry")!.Element(Ns + "Cd")!.Value);
}
