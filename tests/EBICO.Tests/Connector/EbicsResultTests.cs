using AwesomeAssertions;
using EBICO.Connector;

namespace EBICO.Tests.Connector;

/// <summary>Tests for the preliminary <see cref="EbicsResult{T}"/> result/return-code type (issue #46).</summary>
public class EbicsResultTests
{
    [Fact]
    public void Success_SetsValueAndOkReturnCode()
    {
        var result = EbicsResult<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.ReturnCode.Should().Be("000000");
        result.ReturnText.Should().BeNull();
    }

    [Fact]
    public void Success_WithExplicitCodeAndText()
    {
        var result = EbicsResult<string>.Success("data", "011000", "download post-processing done");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("data");
        result.ReturnCode.Should().Be("011000");
        result.ReturnText.Should().Be("download post-processing done");
    }

    [Fact]
    public void Failure_HasNoValue_AndIsNotSuccess()
    {
        var result = EbicsResult<string>.Failure("091005", "no download data available");

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.ReturnCode.Should().Be("091005");
        result.ReturnText.Should().Be("no download data available");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Failure_NullOrEmptyReturnCode_Throws(string? returnCode)
    {
        var act = () => EbicsResult<string>.Failure(returnCode!);

        act.Should().Throw<ArgumentException>();
    }
}
