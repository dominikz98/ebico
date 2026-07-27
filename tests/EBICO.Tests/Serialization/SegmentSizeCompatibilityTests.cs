extern alias EbicoServer;
using AwesomeAssertions;
using EBICO.Core.Serialization;
using EbicoServer::EBICO.Server;

namespace EBICO.Tests.Serialization;

/// <summary>
/// Guards the invariant that made large uploads impossible with the shipped defaults (issue #124): a full
/// order-data segment, once base64-encoded and wrapped in its <c>ebicsRequest</c> envelope, must still fit
/// into the peer's request-body limit.
/// </summary>
/// <remarks>
/// The regression was not that either default was wrong on its own — both were individually tested — but
/// that they were chosen independently. The connector used 768 KiB (the theoretical maximum whose base64
/// form is exactly 1 MiB) while the server accepted 1 MiB of body <em>including</em> the envelope, so any
/// upload big enough to fill one segment was rejected with HTTP 413 before the server could answer with an
/// EBICS return code. These tests pin the relationship between the two numbers rather than the numbers
/// themselves.
/// </remarks>
public class SegmentSizeCompatibilityTests
{
    /// <summary>A generous allowance for the envelope around a segment (header, AuthSignature, order params).</summary>
    private const int EnvelopeAllowanceBytes = 8 * 1024;

    [Fact]
    public void ServerDefaults_LeaveRoomForTheEnvelope()
    {
        var options = new EbicoServerOptions();

        var wireSize = EbicsSegmentation.Base64Length(options.SegmentSizeBytes);

        wireSize.Should().BeLessThan(
            options.MaxRequestBodyBytes,
            "a base64 segment alone must not consume the whole body limit");
        (wireSize + EnvelopeAllowanceBytes).Should().BeLessThanOrEqualTo(
            options.MaxRequestBodyBytes,
            "the envelope travels in the same body as the segment");
    }

    [Fact]
    public void SharedDefault_IsWhatTheServerUses()
    {
        // The connector's upload pipeline defaults to EbicsSegmentation.DefaultSegmentSizeBytes too, so
        // pinning the server to the same constant keeps both sides aligned by construction.
        new EbicoServerOptions().SegmentSizeBytes.Should().Be(EbicsSegmentation.DefaultSegmentSizeBytes);
    }

    [Fact]
    public void TheHistoricalConnectorDefault_WouldNotHaveFit()
    {
        // Documents the actual defect: 768 KiB raw base64-encodes to exactly the 1 MiB body limit, leaving
        // zero bytes for the envelope. Kept as a test so nobody "optimises" the segment size back up.
        const int historicalDefault = 768 * 1024;

        EbicsSegmentation.Base64Length(historicalDefault)
            .Should().Be(new EbicoServerOptions().MaxRequestBodyBytes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(512 * 1024, 699052)]
    public void Base64Length_MatchesTheEncodedSize(int rawBytes, long expected)
    {
        EbicsSegmentation.Base64Length(rawBytes).Should().Be(expected);

        // Cross-check against the framework encoder for the small cases (the large one would allocate).
        if (rawBytes <= 4)
        {
            Convert.ToBase64String(new byte[rawBytes]).Length.Should().Be((int)expected);
        }
    }

    [Fact]
    public void Base64Length_RejectsNegativeInput()
    {
        var act = () => EbicsSegmentation.Base64Length(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaxSegmentSizeForRequestBody_FitsWithinTheLimit()
    {
        const long bodyLimit = 1 * 1024 * 1024;
        const int reserve = 64 * 1024;

        var size = EbicsSegmentation.MaxSegmentSizeForRequestBody(bodyLimit, reserve);

        EbicsSegmentation.Base64Length(size).Should().BeLessThanOrEqualTo(bodyLimit - reserve);
        // And it is the largest such size: one more raw byte would spill over.
        EbicsSegmentation.Base64Length(size + 3).Should().BeGreaterThan(bodyLimit - reserve);
    }

    [Fact]
    public void MaxSegmentSizeForRequestBody_RejectsAReserveThatLeavesNoRoom()
    {
        var act = () => EbicsSegmentation.MaxSegmentSizeForRequestBody(1024, envelopeReserveBytes: 1024);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaxSegmentSizeForRequestBody_RejectsANonPositiveLimit()
    {
        var act = () => EbicsSegmentation.MaxSegmentSizeForRequestBody(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
