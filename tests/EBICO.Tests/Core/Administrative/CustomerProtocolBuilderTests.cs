using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Administrative;

namespace EBICO.Tests.Core.Administrative;

/// <summary>
/// Tests for the customer-protocol builders (issue #41): <see cref="HacProtocolBuilder"/> renders the
/// customer-visible protocol entries as XML, <see cref="PtkProtocolBuilder"/> as text. Both are pure
/// projections over <see cref="CustomerProtocolEntry"/>.
/// </summary>
public class CustomerProtocolBuilderTests
{
    private static readonly CustomerProtocolEntry[] Entries =
    [
        new(1, DateTimeOffset.Parse("2026-07-15T10:00:00Z"), "CCT", "000000", "EBICS_OK", "Info", "Payment accepted"),
        new(2, DateTimeOffset.Parse("2026-07-15T10:05:00Z"), "STA", "090005", "EBICS_NO_DOWNLOAD_DATA_AVAILABLE", "Warning", "No data"),
    ];

    [Fact]
    public void Hac_H005_RendersEntriesInProtocolNamespace()
    {
        var xml = Encoding.UTF8.GetString(HacProtocolBuilder.Build(EbicsVersion.H005, Entries));
        var doc = XDocument.Parse(xml);
        XNamespace ns = "urn:org:ebics:H005";

        doc.Root!.Name.Should().Be(ns + "HACResponseOrderData");
        var entries = doc.Root.Elements(ns + "ProtocolEntry").ToArray();
        entries.Should().HaveCount(2);

        var first = entries[0];
        first.Attribute("sequence")!.Value.Should().Be("1");
        first.Attribute("severity")!.Value.Should().Be("Info");
        first.Element(ns + "OrderType")!.Value.Should().Be("CCT");
        first.Element(ns + "ReturnCode")!.Attribute("symbolic")!.Value.Should().Be("EBICS_OK");
        first.Element(ns + "Message")!.Value.Should().Be("Payment accepted");
    }

    [Fact]
    public void Hac_H003_UsesLegacyNamespace()
    {
        var xml = Encoding.UTF8.GetString(HacProtocolBuilder.Build(EbicsVersion.H003, Entries));
        var doc = XDocument.Parse(xml);

        doc.Root!.Name.Namespace.NamespaceName.Should().Be("http://www.ebics.org/H003");
    }

    [Fact]
    public void Hac_EmptyEntries_ProducesValidEmptyDocument()
    {
        var xml = Encoding.UTF8.GetString(HacProtocolBuilder.Build(EbicsVersion.H005, []));
        var doc = XDocument.Parse(xml);

        doc.Root!.Elements().Should().BeEmpty();
    }

    [Fact]
    public void Ptk_RendersOneLinePerEntry()
    {
        var text = Encoding.UTF8.GetString(PtkProtocolBuilder.Build(Entries));

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().Contain("CCT").And.Contain("EBICS_OK").And.Contain("Payment accepted");
        lines[1].Should().Contain("STA").And.Contain("[Warning]");
    }
}
