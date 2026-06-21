using AwesomeAssertions;
using EBICO.Core;

namespace EBICO.Tests.Infrastructure;

public class SampleXmlTests
{
    [Fact]
    public void XmlFixturesRoot_ExistsNextToTheTestAssembly()
    {
        Directory.Exists(SampleXml.XmlFixturesRoot).Should().BeTrue(
            "the Fixtures/Xml directory structure is copied to the test output");
    }

    [Theory]
    [InlineData(EbicsVersion.H005, SampleDirection.Request, "ebicsRequest_HPB.xml")]
    [InlineData(EbicsVersion.H004, SampleDirection.Response, "ebicsResponse.xml")]
    public void PathFor_BuildsVersionAndDirectionSegments(
        EbicsVersion version, SampleDirection direction, string fileName)
    {
        var path = SampleXml.PathFor(version, direction, fileName);

        path.Should().StartWith(SampleXml.XmlFixturesRoot);
        path.Should().ContainAll(version.ToString(), direction.ToString().ToLowerInvariant(), fileName);
    }

    [Fact]
    public void TryLoad_ReturnsFalse_WhenFixtureMissing()
    {
        var found = SampleXml.TryLoad(
            EbicsVersion.H005, SampleDirection.Request, "does-not-exist.xml", out var xml);

        found.Should().BeFalse();
        xml.Should().BeNull();
    }

    [Fact]
    public void Load_Throws_WhenFixtureMissing()
    {
        var act = () => SampleXml.Load(EbicsVersion.H005, SampleDirection.Request, "does-not-exist.xml");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_ReturnsContent_WhenExamplePresent()
    {
        // Official EBICS examples are proprietary and not committed; this test
        // documents the intended usage and runs only where they are present.
        if (!SampleXml.TryLoad(EbicsVersion.H005, SampleDirection.Request, "ebicsRequest_HPB.xml", out var xml))
        {
            Assert.Skip("EBICS sample-XML not present locally (see Fixtures/Xml/README.md).");
        }

        xml.Should().NotBeNullOrWhiteSpace();
    }
}
