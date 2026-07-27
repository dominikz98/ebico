using AwesomeAssertions;
using EBICO.Core.ReturnCodes;

namespace EBICO.Tests.Core;

/// <summary>
/// Covers <see cref="EbicsReturnCodes.CombineOutcome(string?, string?, string?)"/>, which reduces the two
/// return-code slots of an <c>ebicsResponse</c> to one code <b>and a report text that agrees with it</b>
/// (issue #124).
/// </summary>
/// <remarks>
/// The defect this replaces: the code was taken from whichever slot reported a fault, but the text always
/// came from the header. Since the header carries <c>000000</c>/<c>EBICS_OK</c> whenever the fault sits in
/// the body, every business failure surfaced as e.g. <c>090005</c> with the text <c>EBICS_OK</c>.
/// </remarks>
public class EbicsResponseOutcomeTests
{
    [Fact]
    public void BothSlotsOk_IsSuccessAndKeepsTheHeaderText()
    {
        var outcome = EbicsReturnCodes.CombineOutcome(EbicsReturnCode.OkCode, "EBICS_OK", EbicsReturnCode.OkCode);

        outcome.Code.Should().Be(EbicsReturnCode.OkCode);
        outcome.Text.Should().Be("EBICS_OK");
        outcome.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void TechnicalFault_WinsAndKeepsTheHeaderText()
    {
        var outcome = EbicsReturnCodes.CombineOutcome(
            EbicsReturnCode.AuthenticationFailed.Code, "EBICS_AUTHENTICATION_FAILED", EbicsReturnCode.OkCode);

        outcome.Code.Should().Be(EbicsReturnCode.AuthenticationFailed.Code);
        outcome.Text.Should().Be("EBICS_AUTHENTICATION_FAILED");
        outcome.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void BusinessFault_WinsAndDoesNotInheritTheHeadersOkText()
    {
        // The exact shape a server sends for a business failure: header says OK, body carries the fault.
        var outcome = EbicsReturnCodes.CombineOutcome(
            EbicsReturnCode.OkCode, "EBICS_OK", EbicsReturnCode.NoDownloadDataAvailable.Code);

        outcome.Code.Should().Be(EbicsReturnCode.NoDownloadDataAvailable.Code);
        outcome.Text.Should().NotBe("EBICS_OK", "the text must never contradict a failing code");
        outcome.Text.Should().Be(EbicsReturnCode.NoDownloadDataAvailable.SymbolicName);
        outcome.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void BusinessFault_WithAnUncataloguedCode_ReportsNoText()
    {
        var outcome = EbicsReturnCodes.CombineOutcome(EbicsReturnCode.OkCode, "EBICS_OK", "099999");

        outcome.Code.Should().Be("099999");
        outcome.Text.Should().BeNull("an unknown code has no symbolic name to report");
    }

    [Fact]
    public void TechnicalFault_TakesPrecedenceOverABusinessFault()
    {
        var outcome = EbicsReturnCodes.CombineOutcome(
            EbicsReturnCode.InvalidRequest.Code, "EBICS_INVALID_REQUEST", EbicsReturnCode.InvalidOrderDataFormat.Code);

        outcome.Code.Should().Be(EbicsReturnCode.InvalidRequest.Code);
    }

    [Fact]
    public void MissingSlots_AreTreatedAsOk()
    {
        var outcome = EbicsReturnCodes.CombineOutcome(headerCode: null, headerText: null, bodyCode: null);

        outcome.Code.Should().Be(EbicsReturnCode.OkCode);
        outcome.Text.Should().BeNull();
        outcome.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void TechnicalFault_WithoutAReportText_FallsBackToTheSymbolicName()
    {
        var outcome = EbicsReturnCodes.CombineOutcome(
            EbicsReturnCode.InternalError.Code, headerText: null, bodyCode: EbicsReturnCode.OkCode);

        outcome.Code.Should().Be(EbicsReturnCode.InternalError.Code);
        outcome.Text.Should().Be(EbicsReturnCode.InternalError.SymbolicName);
    }
}
