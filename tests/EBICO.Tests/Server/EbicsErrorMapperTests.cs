using System.Xml;
using AwesomeAssertions;
using EBICO.Core.Versioning;
using EBICO.Server.ReturnCodes;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="EbicsErrorMapper"/> — the central exception → EBICS return-code mapping of
/// the server host skeleton (issue #25).
/// </summary>
public class EbicsErrorMapperTests
{
    private readonly EbicsErrorMapper _mapper = new();

    [Fact]
    public void Map_EnvelopeFormatException_ReturnsInvalidXml()
    {
        _mapper.Map(new EbicsEnvelopeFormatException("malformed"))
            .Should().Be(EbicsReturnCode.InvalidXml);
    }

    [Fact]
    public void Map_VersionNotSupportedException_ReturnsInvalidRequest()
    {
        _mapper.Map(new EbicsVersionNotSupportedException("unknown ns"))
            .Should().Be(EbicsReturnCode.InvalidRequest);
    }

    [Fact]
    public void Map_VersionMismatchException_ReturnsInvalidRequest()
    {
        _mapper.Map(new EbicsVersionMismatchException("mismatch"))
            .Should().Be(EbicsReturnCode.InvalidRequest);
    }

    [Fact]
    public void Map_XmlException_ReturnsInvalidXml()
    {
        _mapper.Map(new XmlException("bad xml"))
            .Should().Be(EbicsReturnCode.InvalidXml);
    }

    [Fact]
    public void Map_DeserializationXmlException_ReturnsInvalidXml()
    {
        // XmlSerializer wraps well-formedness failures in an InvalidOperationException.
        _mapper.Map(new InvalidOperationException("deserialize failed", new XmlException("mismatch")))
            .Should().Be(EbicsReturnCode.InvalidXml);
    }

    [Fact]
    public void Map_UnknownException_ReturnsInternalError()
    {
        _mapper.Map(new InvalidOperationException("boom"))
            .Should().Be(EbicsReturnCode.InternalError);
    }

    [Fact]
    public void Map_Null_Throws()
    {
        var act = () => _mapper.Map(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
