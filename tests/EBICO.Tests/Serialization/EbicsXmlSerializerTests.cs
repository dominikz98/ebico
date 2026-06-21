using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Tests.Infrastructure;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;
using XmlDsig = EBICO.Core.Schema.XmlDsig;

namespace EBICO.Tests.Serialization;

/// <summary>
/// Tier A tests for <see cref="EbicsXmlSerializer"/>: deterministic, clean output across
/// H003/H004/H005, the stable namespace prefix map, the version-dispatched envelope
/// deserialization and the XXE hardening. No proprietary samples required.
/// </summary>
public class EbicsXmlSerializerTests
{
    public static TheoryData<EbicsVersion> VersionCases() =>
        new() { EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005 };

    public static TheoryData<EbicsVersion, string> VersionNamespaceCases() => new()
    {
        { EbicsVersion.H003, "http://www.ebics.org/H003" },
        { EbicsVersion.H004, "urn:org:ebics:H004" },
        { EbicsVersion.H005, "urn:org:ebics:H005" },
    };

    private static IEbicsEnvelope NewRequest(EbicsVersion version) => version switch
    {
        EbicsVersion.H003 => new H003.EbicsRequest { Version = "H003" },
        EbicsVersion.H004 => new H004.EbicsRequest { Version = "H004" },
        EbicsVersion.H005 => new H005.EbicsRequest { Version = "H005" },
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
    };

    // --- Deterministic, clean output ----------------------------------------

    [Theory]
    [MemberData(nameof(VersionNamespaceCases))]
    public void SerializeToUtf8Bytes_IsDeterministicCleanAndUtf8(EbicsVersion version, string namespaceUri)
    {
        var first = EbicsXmlSerializer.SerializeToUtf8Bytes(NewRequest(version));
        var second = EbicsXmlSerializer.SerializeToUtf8Bytes(NewRequest(version));

        first.Should().Equal(second, "the same graph must serialize to identical bytes");

        // No BOM: the first byte is the start of the XML declaration, not 0xEF.
        first[0].Should().Be((byte)'<');

        var text = Encoding.UTF8.GetString(first);
        text.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        text.Should().Contain($"xmlns=\"{namespaceUri}\"");
        text.Should().NotContain("xmlns:xsi");
        text.Should().NotContain("xmlns:xsd");
    }

    [Fact]
    public void Serialize_StructureIsIdenticalAcrossVersions_ModuloNamespaceAndCode()
    {
        string Normalize(EbicsVersion v) =>
            EbicsXmlSerializer.SerializeToString(NewRequest(v))
                .Replace(EbicsVersions.Get(v).NamespaceUri, "<ns>")
                .Replace(v.ToString(), "<ver>");

        var h003 = Normalize(EbicsVersion.H003);

        Normalize(EbicsVersion.H004).Should().Be(h003);
        Normalize(EbicsVersion.H005).Should().Be(h003);
    }

    [Fact]
    public void Serialize_WithAuthSignature_UsesStableDsPrefix()
    {
        var request = new H005.EbicsRequest
        {
            Version = "H005",
            AuthSignature = new XmlDsig.SignatureType
            {
                SignatureValue = new XmlDsig.SignatureValueType { Value = [1, 2, 3] },
            },
        };

        var xml = EbicsXmlSerializer.SerializeToString(request);

        xml.Should().Contain("xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\"");
        xml.Should().Contain("<ds:SignatureValue>AQID</ds:SignatureValue>");
    }

    // --- Round-trip & version-dispatched deserialization --------------------

    [Theory]
    [MemberData(nameof(VersionCases))]
    public void Serialize_Then_DeserializeEnvelope_RoundTrips(EbicsVersion version)
    {
        var request = NewRequest(version);

        var xml = EbicsXmlSerializer.SerializeToString(request);
        var roundTripped = EbicsXmlSerializer.DeserializeEnvelope(xml);

        roundTripped.Should().BeOfType(request.GetType());
        roundTripped.ProtocolVersion.Should().Be(version);
        roundTripped.Version.Should().Be(version.ToString());
        CanonicalXmlComparer.AreEqual(xml, EbicsXmlSerializer.SerializeToString(roundTripped))
            .Should().BeTrue("re-serializing a round-tripped envelope is canonically stable");
    }

    [Theory]
    [MemberData(nameof(VersionNamespaceCases))]
    public void DeserializeEnvelope_DispatchesByNamespaceAndRootElement(EbicsVersion version, string namespaceUri)
    {
        var request = EbicsXmlSerializer.DeserializeEnvelope(
            $"<ebicsRequest xmlns=\"{namespaceUri}\" Version=\"{version}\"/>");
        var response = EbicsXmlSerializer.DeserializeEnvelope(
            $"<ebicsResponse xmlns=\"{namespaceUri}\" Version=\"{version}\"/>");

        request.Should().BeAssignableTo<IEbicsRequestEnvelope>();
        request.ProtocolVersion.Should().Be(version);
        response.Should().BeAssignableTo<IEbicsResponseEnvelope>();
        response.ProtocolVersion.Should().Be(version);
    }

    // --- Hardening & guards -------------------------------------------------

    [Fact]
    public void DeserializeEnvelope_WithDoctype_Throws()
    {
        var act = () => EbicsXmlSerializer.DeserializeEnvelope(
            "<!DOCTYPE ebicsRequest><ebicsRequest xmlns=\"urn:org:ebics:H005\"/>");

        act.Should().Throw<EbicsEnvelopeFormatException>();
    }

    [Fact]
    public void DeserializeEnvelope_UnknownRootElement_Throws()
    {
        var act = () => EbicsXmlSerializer.DeserializeEnvelope(
            "<somethingElse xmlns=\"urn:org:ebics:H005\"/>");

        act.Should().Throw<EbicsEnvelopeFormatException>();
    }

    [Fact]
    public void DeserializeEnvelope_UnknownNamespace_Throws()
    {
        var act = () => EbicsXmlSerializer.DeserializeEnvelope("<ebicsRequest xmlns=\"urn:unknown\"/>");

        act.Should().Throw<EbicsVersionNotSupportedException>();
    }

    [Fact]
    public void SerializeToUtf8Bytes_OnNullEnvelope_Throws()
    {
        var act = () => EbicsXmlSerializer.SerializeToUtf8Bytes((IEbicsEnvelope)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
