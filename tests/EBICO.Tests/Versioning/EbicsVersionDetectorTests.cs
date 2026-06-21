using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Versioning;

namespace EBICO.Tests.Versioning;

/// <summary>
/// Tests for <see cref="EbicsVersionDetector"/> — the inbound version detection that
/// resolves a raw envelope's version from its root namespace and throws the version
/// exceptions for malformed, unsupported or (in strict mode) inconsistent input
/// (issue #14). Tier A: self-contained, hand-authored XML; no proprietary samples.
/// </summary>
public class EbicsVersionDetectorTests
{
    [Theory]
    [InlineData("urn:org:ebics:H005", EbicsVersion.H005)]
    [InlineData("urn:org:ebics:H004", EbicsVersion.H004)]
    [InlineData("http://www.ebics.org/H003", EbicsVersion.H003)]
    public void Detect_KnownRootNamespace_ReturnsVersion(string namespaceUri, EbicsVersion expected)
    {
        var xml = $"<ebicsRequest xmlns=\"{namespaceUri}\"/>";

        EbicsVersionDetector.Detect(xml).Version.Should().Be(expected);
    }

    [Theory]
    [InlineData("ebicsRequest")]
    [InlineData("ebicsResponse")]
    [InlineData("ebicsUnsecuredRequest")]
    [InlineData("ebicsUnsignedRequest")]
    [InlineData("ebicsNoPubKeyDigestsRequest")]
    [InlineData("ebicsKeyManagementResponse")]
    public void Detect_DependsOnNamespaceNotRootLocalName(string rootLocalName)
    {
        var xml = $"<{rootLocalName} xmlns=\"urn:org:ebics:H005\"/>";

        EbicsVersionDetector.Detect(xml).Version.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Detect_FromStream_ReturnsVersion()
    {
        var bytes = Encoding.UTF8.GetBytes("<ebicsRequest xmlns=\"urn:org:ebics:H005\"/>");
        using var stream = new MemoryStream(bytes);

        EbicsVersionDetector.Detect(stream).Version.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Detect_LenientByDefault_IgnoresVersionAttribute()
    {
        // Namespace is authoritative; the free-text @Version attribute is ignored.
        var xml = "<ebicsRequest xmlns=\"urn:org:ebics:H005\" Version=\"H004\"/>";

        EbicsVersionDetector.Detect(xml).Version.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Detect_IgnoresXmlDeclarationAndComments()
    {
        var xml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <!-- leading comment -->
            <ebicsRequest xmlns="urn:org:ebics:H005"/>
            """;

        EbicsVersionDetector.Detect(xml).Version.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Detect_WellFormedRootWithIncompleteBody_DetectsFromRoot()
    {
        // Only the opening root tag is inspected; an unclosed body is a downstream
        // (deserialization) concern, not a detection concern.
        var xml = "<ebicsRequest xmlns=\"urn:org:ebics:H005\"><header>";

        EbicsVersionDetector.Detect(xml).Version.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Detect_StrictWithoutVersionAttribute_ReturnsNamespaceVersion()
    {
        var xml = "<ebicsRequest xmlns=\"urn:org:ebics:H005\"/>";

        EbicsVersionDetector.Detect(xml, strict: true).Version.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Detect_StrictWithMatchingVersion_ReturnsVersion()
    {
        var xml = "<ebicsRequest xmlns=\"urn:org:ebics:H005\" Version=\"H005\"/>";

        EbicsVersionDetector.Detect(xml, strict: true).Version.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Detect_StrictVersionMismatch_ThrowsMismatch()
    {
        var xml = "<ebicsRequest xmlns=\"urn:org:ebics:H005\" Version=\"H004\"/>";

        var act = () => EbicsVersionDetector.Detect(xml, strict: true);

        act.Should().Throw<EbicsVersionMismatchException>();
    }

    [Fact]
    public void Detect_UnknownNamespace_ThrowsNotSupported()
    {
        var act = () => EbicsVersionDetector.Detect("<ebicsRequest xmlns=\"urn:org:ebics:H999\"/>");

        act.Should().Throw<EbicsVersionNotSupportedException>();
    }

    [Fact]
    public void Detect_RootWithoutNamespace_ThrowsNotSupported()
    {
        var act = () => EbicsVersionDetector.Detect("<ebicsRequest/>");

        act.Should().Throw<EbicsVersionNotSupportedException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void Detect_EmptyOrWhitespace_ThrowsFormat(string xml)
    {
        var act = () => EbicsVersionDetector.Detect(xml);

        act.Should().Throw<EbicsEnvelopeFormatException>();
    }

    [Fact]
    public void Detect_NotXml_ThrowsFormat()
    {
        var act = () => EbicsVersionDetector.Detect("this is not xml at all");

        act.Should().Throw<EbicsEnvelopeFormatException>();
    }

    [Fact]
    public void Detect_TruncatedXml_ThrowsFormat()
    {
        // Cut inside the start tag: the parser fails before the root element completes.
        var act = () => EbicsVersionDetector.Detect("<ebicsRequest xmlns=");

        act.Should().Throw<EbicsEnvelopeFormatException>();
    }

    [Fact]
    public void Detect_NoRootElement_ThrowsFormat()
    {
        var xml =
            """
            <?xml version="1.0"?>
            <!-- only a comment, no root element -->
            """;

        var act = () => EbicsVersionDetector.Detect(xml);

        act.Should().Throw<EbicsEnvelopeFormatException>();
    }

    [Fact]
    public void Detect_WithDoctype_ThrowsFormat()
    {
        // DtdProcessing.Prohibit hardens against XXE: a DOCTYPE must be rejected.
        var xml =
            """
            <?xml version="1.0"?>
            <!DOCTYPE ebicsRequest [ <!ENTITY x "y"> ]>
            <ebicsRequest xmlns="urn:org:ebics:H005"/>
            """;

        var act = () => EbicsVersionDetector.Detect(xml);

        act.Should().Throw<EbicsEnvelopeFormatException>();
    }

    [Fact]
    public void Detect_Null_ThrowsArgumentNull()
    {
        var act = () => EbicsVersionDetector.Detect((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryDetect_ValidEnvelope_ReturnsTrueWithInfo()
    {
        EbicsVersionDetector.TryDetect("<ebicsRequest xmlns=\"urn:org:ebics:H005\"/>", out var info)
            .Should().BeTrue();

        info.Should().NotBeNull();
        info!.Version.Should().Be(EbicsVersion.H005);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<ebicsRequest xmlns=\"urn:org:ebics:H999\"/>")]
    public void TryDetect_InvalidEnvelope_ReturnsFalse(string xml)
    {
        EbicsVersionDetector.TryDetect(xml, out var info).Should().BeFalse();

        info.Should().BeNull();
    }

    [Fact]
    public void TryDetect_Null_ThrowsArgumentNull()
    {
        // A null argument is a caller bug, not malformed input, so it is not swallowed.
        var act = () => EbicsVersionDetector.TryDetect((string)null!, out _);

        act.Should().Throw<ArgumentNullException>();
    }
}
