using AwesomeAssertions;
using EBICO.Core.ReturnCodes;

namespace EBICO.Tests.Core.ReturnCodes;

/// <summary>
/// Tests for <see cref="EbicsReturnCode"/> — the value-object catalogue entry of the central EBICS
/// return-code catalogue (issue #36).
/// </summary>
public class EbicsReturnCodeTests
{
    [Fact]
    public void OkCode_IsThreeZeroPairs()
    {
        EbicsReturnCode.OkCode.Should().Be("000000");
    }

    [Fact]
    public void Ok_HasExpectedFields()
    {
        EbicsReturnCode.Ok.Code.Should().Be("000000");
        EbicsReturnCode.Ok.SymbolicName.Should().Be("EBICS_OK");
        EbicsReturnCode.Ok.Kind.Should().Be(EbicsReturnCodeKind.Technical);
    }

    [Fact]
    public void InvalidXml_HasExpectedFields()
    {
        EbicsReturnCode.InvalidXml.Code.Should().Be("091010");
        EbicsReturnCode.InvalidXml.SymbolicName.Should().Be("EBICS_INVALID_XML");
        EbicsReturnCode.InvalidXml.Kind.Should().Be(EbicsReturnCodeKind.Business);
    }

    [Fact]
    public void RecordStruct_HasValueEquality()
    {
        var equivalent = new EbicsReturnCode("091010", "EBICS_INVALID_XML", EbicsReturnCodeKind.Business);

        equivalent.Should().Be(EbicsReturnCode.InvalidXml);
        (equivalent == EbicsReturnCode.InvalidXml).Should().BeTrue();
    }
}
