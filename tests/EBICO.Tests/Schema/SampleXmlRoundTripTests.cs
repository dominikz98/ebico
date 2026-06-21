using System.Xml.Serialization;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Tests.Infrastructure;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Schema;

/// <summary>
/// Tier B: validates the generated bindings against <b>real</b> EBICS example XML.
/// <para>
/// The official examples (ebics.org) are proprietary and are not committed
/// (see <see cref="SampleXml"/> and ADR-0003), so these tests <see cref="Assert.Skip(string)"/>
/// when the fixtures are absent — keeping CI green. Drop a sample named
/// <c>ebicsRequest.xml</c> / <c>ebicsResponse.xml</c> under
/// <c>Fixtures/Xml/&lt;version&gt;/&lt;direction&gt;/</c> to activate them locally.
/// </para>
/// <para>
/// The check is two-fold: the binding must <i>deserialize</i> the real payload
/// without error, and our own re-serialization must be canonically stable. We do
/// not compare against the original file's canonical form, because Canonical XML
/// keeps namespace <em>prefixes</em> and those legitimately differ between the
/// bank's serializer and ours.
/// </para>
/// </summary>
public class SampleXmlRoundTripTests
{
    [Theory]
    [InlineData(EbicsVersion.H003, "ebicsRequest.xml")]
    [InlineData(EbicsVersion.H004, "ebicsRequest.xml")]
    [InlineData(EbicsVersion.H005, "ebicsRequest.xml")]
    public void RealRequestSample_DeserializesAndReserializesStably(EbicsVersion version, string fileName)
    {
        AssertSampleRoundTrips(version, SampleDirection.Request, fileName, RequestType(version));
    }

    [Theory]
    [InlineData(EbicsVersion.H003, "ebicsResponse.xml")]
    [InlineData(EbicsVersion.H004, "ebicsResponse.xml")]
    [InlineData(EbicsVersion.H005, "ebicsResponse.xml")]
    public void RealResponseSample_DeserializesAndReserializesStably(EbicsVersion version, string fileName)
    {
        AssertSampleRoundTrips(version, SampleDirection.Response, fileName, ResponseType(version));
    }

    private static void AssertSampleRoundTrips(
        EbicsVersion version, SampleDirection direction, string fileName, Type rootType)
    {
        if (!SampleXml.TryLoad(version, direction, fileName, out var xml))
        {
            Assert.Skip(
                $"Sample {version}/{direction}/{fileName} not present (proprietary, not committed).");
        }

        var serializer = new XmlSerializer(rootType);

        object graph;
        using (var reader = new StringReader(xml))
        {
            graph = serializer.Deserialize(reader)!;
        }

        graph.Should().NotBeNull();

        string first;
        using (var writer = new StringWriter())
        {
            serializer.Serialize(writer, graph);
            first = writer.ToString();
        }

        string second;
        using (var reader = new StringReader(first))
        using (var writer = new StringWriter())
        {
            serializer.Serialize(writer, serializer.Deserialize(reader));
            second = writer.ToString();
        }

        CanonicalXmlComparer.AreEqual(first, second)
            .Should().BeTrue("our serialization of the {0} {1} sample must be deterministic", version, direction);
    }

    private static Type RequestType(EbicsVersion version) => version switch
    {
        EbicsVersion.H003 => typeof(H003.EbicsRequest),
        EbicsVersion.H004 => typeof(H004.EbicsRequest),
        EbicsVersion.H005 => typeof(H005.EbicsRequest),
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
    };

    private static Type ResponseType(EbicsVersion version) => version switch
    {
        EbicsVersion.H003 => typeof(H003.EbicsResponse),
        EbicsVersion.H004 => typeof(H004.EbicsResponse),
        EbicsVersion.H005 => typeof(H005.EbicsResponse),
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
    };
}
