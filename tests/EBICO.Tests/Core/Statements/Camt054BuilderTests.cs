using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>Tests for <see cref="Camt054Builder"/> (C54): namespace, notification root, entries and no balances.</summary>
public class Camt054BuilderTests
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:camt.054.001.08";

    [Fact]
    public void Build_ProducesCamt054_WithEntriesAndNoBalances()
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(Camt054Builder.Build(StatementSampleData.WithTwoEntries())));

        doc.Root!.Name.Should().Be(Ns + "Document");
        doc.Descendants(Ns + "BkToCstmrDbtCdtNtfctn").Should().ContainSingle();
        doc.Descendants(Ns + "Ntry").Should().HaveCount(2);
        doc.Descendants(Ns + "Bal").Should().BeEmpty();
    }
}
