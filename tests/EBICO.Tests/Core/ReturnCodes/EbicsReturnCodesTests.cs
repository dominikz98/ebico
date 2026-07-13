using AwesomeAssertions;
using EBICO.Core.ReturnCodes;

namespace EBICO.Tests.Core.ReturnCodes;

/// <summary>
/// Tests for <see cref="EbicsReturnCodes"/> — the central EBICS return-code registry (issue #36).
/// Known-answer values are pinned against EBICS Annex 1, not just self-consistency.
/// </summary>
public class EbicsReturnCodesTests
{
    [Fact]
    public void All_IsNotEmpty()
    {
        EbicsReturnCodes.All.Should().NotBeEmpty();
    }

    [Fact]
    public void All_CodesAreSixDigits()
    {
        EbicsReturnCodes.All.Should().OnlyContain(rc => rc.Code.Length == 6);
        foreach (var rc in EbicsReturnCodes.All)
        {
            rc.Code.Should().MatchRegex(@"^\d{6}$");
        }
    }

    [Fact]
    public void All_CodesAreUnique()
    {
        EbicsReturnCodes.All.Select(rc => rc.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_HaveNonEmptySymbolicName()
    {
        EbicsReturnCodes.All.Should().OnlyContain(rc => !string.IsNullOrWhiteSpace(rc.SymbolicName));
    }

    // --- Known-answer vectors (code -> symbolic name + kind), pinned against EBICS Annex 1 ---

    [Theory]
    [InlineData("000000", "EBICS_OK", EbicsReturnCodeKind.Technical)]
    [InlineData("011000", "EBICS_DOWNLOAD_POSTPROCESS_DONE", EbicsReturnCodeKind.Technical)]
    [InlineData("061001", "EBICS_AUTHENTICATION_FAILED", EbicsReturnCodeKind.Technical)]
    [InlineData("061002", "EBICS_INVALID_REQUEST", EbicsReturnCodeKind.Technical)]
    [InlineData("061099", "EBICS_INTERNAL_ERROR", EbicsReturnCodeKind.Technical)]
    [InlineData("090004", "EBICS_INVALID_ORDER_DATA_FORMAT", EbicsReturnCodeKind.Business)]
    [InlineData("091002", "EBICS_INVALID_USER_OR_USER_STATE", EbicsReturnCodeKind.Business)]
    [InlineData("091005", "EBICS_INVALID_ORDER_TYPE", EbicsReturnCodeKind.Business)]
    [InlineData("091006", "EBICS_UNSUPPORTED_ORDER_TYPE", EbicsReturnCodeKind.Business)]
    [InlineData("091010", "EBICS_INVALID_XML", EbicsReturnCodeKind.Business)]
    [InlineData("091101", "EBICS_TX_UNKNOWN_TXID", EbicsReturnCodeKind.Business)]
    public void Get_KnownCode_ReturnsExpectedEntry(string code, string symbolicName, EbicsReturnCodeKind kind)
    {
        var returnCode = EbicsReturnCodes.Get(code);

        returnCode.Code.Should().Be(code);
        returnCode.SymbolicName.Should().Be(symbolicName);
        returnCode.Kind.Should().Be(kind);
    }

    [Fact]
    public void Get_UnknownCode_Throws()
    {
        var act = () => EbicsReturnCodes.Get("999999");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TryFromCode_KnownCode_ReturnsTrueAndEntry()
    {
        EbicsReturnCodes.TryFromCode("091010", out var returnCode).Should().BeTrue();
        returnCode.Should().Be(EbicsReturnCode.InvalidXml);
    }

    [Theory]
    [InlineData("999999")]
    [InlineData("")]
    [InlineData(null)]
    public void TryFromCode_UnknownCode_ReturnsFalse(string? code)
    {
        EbicsReturnCodes.TryFromCode(code, out var returnCode).Should().BeFalse();
        returnCode.Should().Be(default(EbicsReturnCode));
    }

    [Fact]
    public void IsSuccess_OkCode_IsTrue()
    {
        EbicsReturnCodes.IsSuccess("000000").Should().BeTrue();
    }

    [Theory]
    [InlineData("090004")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSuccess_NonOkCode_IsFalse(string? code)
    {
        EbicsReturnCodes.IsSuccess(code).Should().BeFalse();
    }
}
