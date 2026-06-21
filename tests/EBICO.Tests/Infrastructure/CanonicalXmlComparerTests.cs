using System.Xml;
using AwesomeAssertions;

namespace EBICO.Tests.Infrastructure;

public class CanonicalXmlComparerTests
{
    [Fact]
    public void AreEqual_IgnoresInsignificantWhitespaceAndIndentation()
    {
        const string compact = "<root><child id=\"1\">text</child></root>";
        const string indented = "<root>\n  <child id=\"1\">text</child>\n</root>";

        CanonicalXmlComparer.AreEqual(compact, indented).Should().BeTrue();
    }

    [Fact]
    public void AreEqual_IgnoresAttributeOrdering()
    {
        const string a = "<e x=\"1\" y=\"2\"></e>";
        const string b = "<e y=\"2\" x=\"1\"></e>";

        CanonicalXmlComparer.AreEqual(a, b).Should().BeTrue();
    }

    [Fact]
    public void AreEqual_TreatsEmptyElementAndExplicitCloseAsEqual()
    {
        CanonicalXmlComparer.AreEqual("<e/>", "<e></e>").Should().BeTrue();
    }

    [Fact]
    public void AreEqual_DetectsDifferentElementContent()
    {
        const string a = "<root><child>A</child></root>";
        const string b = "<root><child>B</child></root>";

        CanonicalXmlComparer.AreEqual(a, b).Should().BeFalse();
    }

    [Fact]
    public void AreEqual_DetectsDifferentAttributeValue()
    {
        CanonicalXmlComparer.AreEqual("<e x=\"1\"/>", "<e x=\"2\"/>").Should().BeFalse();
    }

    [Fact]
    public void Canonicalize_IsStableAcrossFormattingDifferences()
    {
        var first = CanonicalXmlComparer.Canonicalize("<a><b/></a>");
        var second = CanonicalXmlComparer.Canonicalize("<a>\n    <b></b>\n</a>");

        second.Should().Be(first);
    }

    [Fact]
    public void Canonicalize_OnNull_Throws()
    {
        var act = () => CanonicalXmlComparer.Canonicalize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Canonicalize_OnMalformedXml_Throws()
    {
        var act = () => CanonicalXmlComparer.Canonicalize("<root><unclosed></root>");

        act.Should().Throw<XmlException>();
    }
}
