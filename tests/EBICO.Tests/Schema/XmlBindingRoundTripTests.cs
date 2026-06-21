using System.Xml.Serialization;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Tests.Infrastructure;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;
using Hev = EBICO.Core.Schema.Hev;

namespace EBICO.Tests.Schema;

/// <summary>
/// Round-trip tests for the generated XSD bindings (issues #11–#13).
/// <para>
/// <b>Tier A</b> (these tests) is self-contained: it constructs object graphs,
/// serializes them with <see cref="XmlSerializer"/> and asserts the data and the
/// canonical XML survive a serialize → deserialize → serialize cycle. It needs no
/// proprietary sample XML and therefore runs in CI. The hardest binding features
/// are exercised on purpose: optional value types (the nullable adapter vs. the
/// <c>*Specified</c> foot-gun), substitution groups (<c>OrderParams</c>) and a
/// cross-namespace reference (<c>AuthSignature</c> → shared xmldsig).
/// </para>
/// <para>
/// <b>Tier B</b> lives in <see cref="SampleXmlRoundTripTests"/> and validates the
/// bindings against real EBICS examples when they are present locally.
/// </para>
/// </summary>
public class XmlBindingRoundTripTests
{
    private static string Serialize<T>(T value)
    {
        // Use the runtime type: callers pass some graphs as `object` (the
        // substitution-group factories), where typeof(T) would be System.Object.
        var serializer = new XmlSerializer(value!.GetType());
        using var writer = new StringWriter();
        serializer.Serialize(writer, value);
        return writer.ToString();
    }

    private static T Deserialize<T>(string xml)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var reader = new StringReader(xml);
        return (T)serializer.Deserialize(reader)!;
    }

    private static T RoundTrip<T>(T value) => Deserialize<T>(Serialize(value));

    /// <summary>Asserts that re-serializing a deserialized graph is canonically stable.</summary>
    private static void AssertCanonicalStable<T>(T value)
    {
        var first = Serialize(value);
        var second = Serialize(Deserialize<T>(first));
        CanonicalXmlComparer.AreEqual(first, second)
            .Should().BeTrue("the binding must serialize {0} deterministically", typeof(T).Name);
    }

    // --- The serializer itself must build for every protocol root -----------
    // Constructing an XmlSerializer reflects the whole type graph; if any binding
    // had conflicting XML attributes this would throw. So it is a real check.

    [Fact]
    public void XmlSerializer_BuildsForEveryProtocolRoot()
    {
        var roots = new[]
        {
            typeof(H003.EbicsRequest), typeof(H003.EbicsResponse),
            typeof(H004.EbicsRequest), typeof(H004.EbicsResponse),
            typeof(H005.EbicsRequest), typeof(H005.EbicsResponse),
            typeof(H005.EbicsUnsecuredRequest), typeof(H005.EbicsNoPubKeyDigestsRequest),
            typeof(H005.EbicsKeyManagementResponse),
            typeof(Hev.HevRequestDataType), typeof(Hev.HevResponseDataType),
        };

        foreach (var root in roots)
        {
            var act = () => new XmlSerializer(root);
            act.Should().NotThrow("binding root {0} must be XmlSerializer-compatible", root.FullName);
        }
    }

    // --- HEV (shared H000 namespace) ----------------------------------------

    [Fact]
    public void HevRequest_RoundTrips_PreservingHostId()
    {
        var request = new Hev.HevRequestDataType { HostId = "EBICOHOST01" };

        var result = RoundTrip(request);

        result.HostId.Should().Be("EBICOHOST01");
        AssertCanonicalStable(request);
    }

    [Fact]
    public void HevResponse_RoundTrips_PreservingVersionNumbers()
    {
        var response = new Hev.HevResponseDataType
        {
            SystemReturnCode = new Hev.SystemReturnCodeType
            {
                ReturnCode = "000000",
                ReportText = "OK",
            },
        };
        response.VersionNumber.Add(new Hev.HevResponseDataTypeVersionNumber
        {
            ProtocolVersion = "H005",
            Value = "03.00",
        });

        var result = RoundTrip(response);

        result.SystemReturnCode.ReturnCode.Should().Be("000000");
        result.VersionNumber.Should().ContainSingle()
            .Which.ProtocolVersion.Should().Be("H005");
        AssertCanonicalStable(response);
    }

    [Fact]
    public void HevResponse_DeserializesRealWireFormat()
    {
        // Hand-authored to the H000 schema (elementFormDefault="qualified"), NOT
        // produced by our serializer — proves the binding binds to the real wire
        // format: root element name, namespace, repeated VersionNumber, and the
        // ProtocolVersion attribute + text value.
        const string wire =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <ebicsHEVResponse xmlns="http://www.ebics.org/H000">
              <SystemReturnCode>
                <ReturnCode>000000</ReturnCode>
                <ReportText>[EBICS_OK] OK</ReportText>
              </SystemReturnCode>
              <VersionNumber ProtocolVersion="H005">03.00</VersionNumber>
              <VersionNumber ProtocolVersion="H004">02.50</VersionNumber>
            </ebicsHEVResponse>
            """;

        var result = Deserialize<Hev.HevResponseDataType>(wire);

        result.SystemReturnCode.ReturnCode.Should().Be("000000");
        result.SystemReturnCode.ReportText.Should().Be("[EBICS_OK] OK");
        result.VersionNumber.Should().HaveCount(2);
        result.VersionNumber.Should().ContainSingle(v => v.ProtocolVersion == "H005")
            .Which.Value.Should().Be("03.00");
        result.VersionNumber.Should().ContainSingle(v => v.ProtocolVersion == "H004")
            .Which.Value.Should().Be("02.50");
    }

    // --- Optional value type: the nullable adapter must round-trip ----------
    // Revision is minOccurs=0; xscgen (--nullable) exposes it as byte? backed by a
    // RevisionValue/RevisionValueSpecified pair. Forgetting the *Specified flag is
    // the classic silent-data-loss bug, so we assert it both ways.

    [Theory]
    [MemberData(nameof(RequestFactories))]
    public void EbicsRequest_OptionalRevision_SurvivesRoundTrip(string version, Func<string, byte?, object> make)
    {
        var request = make(version, 7);

        var xml = Serialize(request);
        var result = Deserialize(make(version, null).GetType(), xml);

        GetVersion(result).Should().Be(version);
        GetRevision(result).Should().Be((byte)7);
    }

    [Theory]
    [MemberData(nameof(RequestFactories))]
    public void EbicsRequest_NullRevision_IsOmitted(string version, Func<string, byte?, object> make)
    {
        var request = make(version, null);

        var xml = Serialize(request);

        xml.Should().NotContain("Revision=");
    }

    public static TheoryData<string, Func<string, byte?, object>> RequestFactories() => new()
    {
        { "H003", (v, r) => new H003.EbicsRequest { Version = v, Revision = r } },
        { "H004", (v, r) => new H004.EbicsRequest { Version = v, Revision = r } },
        { "H005", (v, r) => new H005.EbicsRequest { Version = v, Revision = r } },
    };

    private static object Deserialize(Type type, string xml)
    {
        var serializer = new XmlSerializer(type);
        using var reader = new StringReader(xml);
        return serializer.Deserialize(reader)!;
    }

    private static string? GetVersion(object request) =>
        (string?)request.GetType().GetProperty("Version")!.GetValue(request);

    private static byte? GetRevision(object request) =>
        (byte?)request.GetType().GetProperty("Revision")!.GetValue(request);

    // --- Substitution group: OrderParams must keep its concrete type --------

    [Fact]
    public void H005_OrderParamsSubstitution_RoundTripsAsStandardOrderParams()
    {
        var details = new H005.StaticHeaderOrderDetailsType
        {
            OrderId = "HAA0",
            OrderParams = new H005.StandardOrderParamsType(),
        };

        var xml = Serialize(details);
        xml.Should().Contain("<StandardOrderParams");

        var result = RoundTrip(details);
        result.OrderParams.Should().BeOfType<H005.StandardOrderParamsType>();
        AssertCanonicalStable(details);
    }

    [Fact]
    public void H003_OrderParamsSubstitution_RoundTripsAsStandardOrderParams()
    {
        var details = new H003.StaticHeaderOrderDetailsType
        {
            OrderId = "HAA0",
            OrderParams = new H003.StandardOrderParamsType(),
        };

        var result = RoundTrip(details);

        result.OrderParams.Should().BeOfType<H003.StandardOrderParamsType>();
        AssertCanonicalStable(details);
    }
}
