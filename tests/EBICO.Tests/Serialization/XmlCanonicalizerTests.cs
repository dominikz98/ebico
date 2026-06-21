using System.Text;
using System.Xml;
using AwesomeAssertions;
using EBICO.Core.Serialization;

namespace EBICO.Tests.Serialization;

/// <summary>
/// Tests for the production canonicalizer (<see cref="XmlCanonicalizer"/>) against known
/// W3C C14N behaviours. Tier A: self-contained, CI-safe vectors (DTD-free, since DTDs are
/// rejected by design). The vectors are adapted from the W3C Canonical XML 1.0 (§3) and
/// Exclusive XML Canonicalization specifications — these are <b>not</b> EBICS-proprietary.
/// </summary>
public class XmlCanonicalizerTests
{
    // --- Inclusive C14N normalisation rules ---------------------------------

    [Fact]
    public void Canonicalize_ExpandsEmptyElementToStartEndTags()
    {
        XmlCanonicalizer.CanonicalizeToString("<e/>").Should().Be("<e></e>");
    }

    [Fact]
    public void Canonicalize_SortsAttributesAndIsOrderInsensitive()
    {
        var a = XmlCanonicalizer.Canonicalize("<e b=\"2\" a=\"1\"/>");
        var b = XmlCanonicalizer.Canonicalize("<e a=\"1\" b=\"2\"></e>");

        a.Should().Equal(b);
        XmlCanonicalizer.CanonicalizeToString("<e b=\"2\" a=\"1\"/>").Should().Be("<e a=\"1\" b=\"2\"></e>");
    }

    [Fact]
    public void Canonicalize_EscapesSpecialCharactersInTextContent()
    {
        // C14N escapes '>' as &gt; in text content even though XML does not require it.
        XmlCanonicalizer.CanonicalizeToString("<a>a > b</a>").Should().Be("<a>a &gt; b</a>");
    }

    [Fact]
    public void Canonicalize_DropsXmlDeclaration()
    {
        XmlCanonicalizer.CanonicalizeToString("<?xml version=\"1.0\" encoding=\"utf-8\"?><a/>")
            .Should().Be("<a></a>");
    }

    [Fact]
    public void Canonicalize_IsWhitespaceFaithful()
    {
        // Unlike the whitespace-tolerant test comparer, the production canonicalizer keeps
        // significant whitespace, because the canonical octets are the signed material.
        XmlCanonicalizer.CanonicalizeToString("<a> <b/> </a>").Should().Be("<a> <b></b> </a>");

        var spaced = XmlCanonicalizer.Canonicalize("<a> <b/> </a>");
        var tight = XmlCanonicalizer.Canonicalize("<a><b/></a>");
        spaced.Should().NotEqual(tight);
    }

    [Fact]
    public void Canonicalize_OutputIsUtf8Octets()
    {
        // U+00E4 (LATIN SMALL LETTER A WITH DIAERESIS) encodes to 0xC3 0xA4 in UTF-8.
        // Built from the code point so the source file stays pure ASCII.
        var input = "<a>" + (char)0x00E4 + "</a>";

        var bytes = XmlCanonicalizer.Canonicalize(input);

        bytes.Should().ContainInOrder((byte)0xC3, (byte)0xA4);
        Encoding.UTF8.GetString(bytes).Should().Be(input);
    }

    [Fact]
    public void Canonicalize_IsDeterministic()
    {
        const string xml = "<r><c y=\"2\" x=\"1\">t</c><c/></r>";

        XmlCanonicalizer.Canonicalize(xml).Should().Equal(XmlCanonicalizer.Canonicalize(xml));
    }

    // --- Comments are kept only in the WithComments modes -------------------

    [Fact]
    public void Canonicalize_Inclusive_DropsComments()
    {
        XmlCanonicalizer.CanonicalizeToString("<a><!--c--><b/></a>", C14nMode.Inclusive)
            .Should().Be("<a><b></b></a>");
    }

    [Fact]
    public void Canonicalize_InclusiveWithComments_KeepsComments()
    {
        XmlCanonicalizer.CanonicalizeToString("<a><!--c--><b/></a>", C14nMode.InclusiveWithComments)
            .Should().Be("<a><!--c--><b></b></a>");
    }

    // --- The exclusive-vs-inclusive differentiator (the core vector) --------

    private const string UnusedNamespaceDoc =
        "<n0:a xmlns:n0=\"http://a\" xmlns:n1=\"http://b\"><n0:child/></n0:a>";

    [Fact]
    public void Canonicalize_Inclusive_KeepsUnusedAncestorNamespace()
    {
        XmlCanonicalizer.CanonicalizeToString(UnusedNamespaceDoc, C14nMode.Inclusive)
            .Should().Be("<n0:a xmlns:n0=\"http://a\" xmlns:n1=\"http://b\"><n0:child></n0:child></n0:a>");
    }

    [Fact]
    public void Canonicalize_Exclusive_DropsUnusedNamespace()
    {
        XmlCanonicalizer.CanonicalizeToString(UnusedNamespaceDoc, C14nMode.Exclusive)
            .Should().Be("<n0:a xmlns:n0=\"http://a\"><n0:child></n0:child></n0:a>");
    }

    [Fact]
    public void Canonicalize_InclusiveAndExclusive_DifferOnUnusedNamespace()
    {
        var inclusive = XmlCanonicalizer.Canonicalize(UnusedNamespaceDoc, C14nMode.Inclusive);
        var exclusive = XmlCanonicalizer.Canonicalize(UnusedNamespaceDoc, C14nMode.Exclusive);

        inclusive.Should().NotEqual(exclusive);
    }

    [Fact]
    public void Canonicalize_Exclusive_InclusivePrefixList_KeepsListedNamespace()
    {
        // Forcing n1 into the InclusiveNamespaces PrefixList makes exclusive keep it.
        XmlCanonicalizer.CanonicalizeToString(UnusedNamespaceDoc, C14nMode.Exclusive, "n1")
            .Should().Be("<n0:a xmlns:n0=\"http://a\" xmlns:n1=\"http://b\"><n0:child></n0:child></n0:a>");
    }

    // --- Hardening & guards -------------------------------------------------

    [Fact]
    public void Canonicalize_RejectsDoctype()
    {
        var act = () => XmlCanonicalizer.Canonicalize("<!DOCTYPE a><a/>");

        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void Canonicalize_OnNull_Throws()
    {
        var act = () => XmlCanonicalizer.Canonicalize((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Canonicalize_OnMalformedXml_Throws()
    {
        var act = () => XmlCanonicalizer.Canonicalize("<a><b></a>");

        act.Should().Throw<XmlException>();
    }
}

/// <summary>Tests for the canonicalization algorithm-URI mapping (<see cref="C14nAlgorithms"/>).</summary>
public class C14nAlgorithmsTests
{
    [Theory]
    [InlineData(C14nMode.Inclusive)]
    [InlineData(C14nMode.InclusiveWithComments)]
    [InlineData(C14nMode.Exclusive)]
    [InlineData(C14nMode.ExclusiveWithComments)]
    public void AlgorithmUri_RoundTrips(C14nMode mode)
    {
        C14nAlgorithms.FromAlgorithmUri(C14nAlgorithms.ToAlgorithmUri(mode)).Should().Be(mode);
    }

    [Fact]
    public void Constants_MatchTheW3CUris()
    {
        C14nAlgorithms.Inclusive.Should().Be("http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
        C14nAlgorithms.Exclusive.Should().Be("http://www.w3.org/2001/10/xml-exc-c14n#");
    }

    [Fact]
    public void FromAlgorithmUri_Unknown_Throws()
    {
        var act = () => C14nAlgorithms.FromAlgorithmUri("urn:not-a-c14n-algorithm");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromAlgorithmUri_Null_Throws()
    {
        var act = () => C14nAlgorithms.FromAlgorithmUri(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
