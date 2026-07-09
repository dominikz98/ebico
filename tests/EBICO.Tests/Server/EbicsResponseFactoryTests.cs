using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.ReturnCodes;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="EbicsResponseFactory"/> — building well-formed, per-version
/// <c>ebicsResponse</c> envelopes that carry an EBICS return code (issue #25). Uses the committed
/// Core bindings; no proprietary sample XML.
/// </summary>
public class EbicsResponseFactoryTests
{
    private readonly EbicsResponseFactory _factory = new();

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildErrorResponse_RoundTripsToResponseOfSameVersion(EbicsVersion version)
    {
        var envelope = _factory.BuildErrorResponse(version, EbicsReturnCode.UnsupportedOrderType);

        var xml = EbicsXmlSerializer.SerializeToString(envelope);
        var parsed = EbicsXmlSerializer.DeserializeEnvelope(xml);

        parsed.Should().BeAssignableTo<IEbicsResponseEnvelope>();
        parsed.ProtocolVersion.Should().Be(version);
    }

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildErrorResponse_BusinessCode_GoesToBodyReturnCode(EbicsVersion version)
    {
        var envelope = _factory.BuildErrorResponse(version, EbicsReturnCode.UnsupportedOrderType);

        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);

        bodyCode.Should().Be("091006");
        headerCode.Should().Be(EbicsReturnCode.OkCode);
    }

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildErrorResponse_TechnicalCode_GoesToHeaderReturnCode(EbicsVersion version)
    {
        var envelope = _factory.BuildErrorResponse(version, EbicsReturnCode.InvalidRequest);

        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);

        headerCode.Should().Be("061002");
        bodyCode.Should().Be(EbicsReturnCode.OkCode);
    }

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildErrorResponse_UsesVersionNamespace(EbicsVersion version)
    {
        var envelope = _factory.BuildErrorResponse(version, EbicsReturnCode.InvalidXml);

        var xml = EbicsXmlSerializer.SerializeToString(envelope);

        xml.Should().Contain(EbicsVersions.Get(version).NamespaceUri);
    }

    [Fact]
    public void BuildErrorResponse_TechnicalCode_ReportsSymbolicNameAsReportText()
    {
        var envelope = _factory.BuildErrorResponse(EbicsVersion.H004, EbicsReturnCode.InvalidRequest);

        var response = (EBICO.Core.Schema.H004.EbicsResponse)envelope;
        response.Header.Mutable.ReportText.Should().Be("EBICS_INVALID_REQUEST");
    }

    [Fact]
    public void BuildErrorResponse_BusinessCode_HeaderReportTextStaysConsistentWithOkHeaderCode()
    {
        // For a business code the header code is 000000, so its ReportText must not contradict it.
        var envelope = _factory.BuildErrorResponse(EbicsVersion.H004, EbicsReturnCode.UnsupportedOrderType);

        var response = (EBICO.Core.Schema.H004.EbicsResponse)envelope;
        response.Header.Mutable.ReturnCode.Should().Be(EbicsReturnCode.OkCode);
        response.Header.Mutable.ReportText.Should().Be("EBICS_OK");
    }
}
