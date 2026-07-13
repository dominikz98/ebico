using AwesomeAssertions;
using EBICO.Core.Serialization;

namespace EBICO.Tests.Serialization;

/// <summary>Tests for the EBICS order-data segmentation helper (issue #34).</summary>
public class EbicsSegmentationTests
{
    // Deterministic, mildly varied payload (mirrors EbicsCompressionTests).
    private static byte[] Bytes(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i * 31 % 251);
        }

        return data;
    }

    [Fact]
    public void Split_ExactMultiple_ProducesEqualSizedSegments()
    {
        var result = EbicsSegmentation.Split(Bytes(12), 4);

        result.NumSegments.Should().Be(3);
        result.Segments.Should().OnlyContain(s => s.Length == 4);
    }

    [Fact]
    public void Split_WithRemainder_LastSegmentShorter()
    {
        var result = EbicsSegmentation.Split(Bytes(10), 4);

        result.NumSegments.Should().Be(3);
        result.Segments[0].Should().HaveCount(4);
        result.Segments[1].Should().HaveCount(4);
        result.Segments[2].Should().HaveCount(2);
    }

    [Fact]
    public void Split_SmallerThanSize_SingleSegment()
    {
        var payload = Bytes(10);

        var result = EbicsSegmentation.Split(payload, 1024);

        result.NumSegments.Should().Be(1);
        result.Segments[0].Should().Equal(payload);
    }

    [Fact]
    public void Split_HugeSegmentSize_SingleSegment()
    {
        // Guards against ceil-division overflow: a large size must still yield one segment,
        // not zero (which would silently drop the payload).
        var payload = Bytes(100);

        var result = EbicsSegmentation.Split(payload, int.MaxValue);

        result.NumSegments.Should().Be(1);
        result.Segments[0].Should().Equal(payload);
    }

    [Fact]
    public void Split_Empty_ProducesSingleEmptySegment()
    {
        var result = EbicsSegmentation.Split([], 1024);

        result.NumSegments.Should().Be(1);
        result.Segments[0].Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Split_NonPositiveSize_Throws(int size)
    {
        var act = () => EbicsSegmentation.Split(Bytes(8), size);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Split_IsDeterministic()
    {
        var payload = Bytes(1000);

        var first = EbicsSegmentation.Split(payload, 64);
        var second = EbicsSegmentation.Split(payload, 64);

        first.NumSegments.Should().Be(second.NumSegments);
        for (var i = 0; i < first.NumSegments; i++)
        {
            first.Segments[i].Should().Equal(second.Segments[i]);
        }
    }

    [Fact]
    public void Split_KnownVector_ProducesExpectedBoundaries()
    {
        // Pinned split boundaries: 10 bytes at size 4 → [0..3][4..7][8..9].
        var payload = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var result = EbicsSegmentation.Split(payload, 4);

        result.NumSegments.Should().Be(3);
        result.Segments[0].Should().Equal((byte)0, 1, 2, 3);
        result.Segments[1].Should().Equal((byte)4, 5, 6, 7);
        result.Segments[2].Should().Equal((byte)8, 9);
    }

    [Fact]
    public void Reassemble_Null_Throws()
    {
        var act = () => EbicsSegmentation.Reassemble(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reassemble_EmptyList_Throws()
    {
        var act = () => EbicsSegmentation.Reassemble([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reassemble_NullElement_Throws()
    {
        byte[][] segments = [[1, 2], null!];

        var act = () => EbicsSegmentation.Reassemble(segments);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reassemble_SingleEmptySegment_RestoresEmpty()
    {
        var restored = EbicsSegmentation.Reassemble([[]]);

        restored.Should().BeEmpty();
    }

    [Fact]
    public void Reassemble_PreservesOrder()
    {
        byte[][] segments = [[1, 2], [3], [4, 5, 6]];

        var restored = EbicsSegmentation.Reassemble(segments);

        restored.Should().Equal((byte)1, 2, 3, 4, 5, 6);
    }

    public static TheoryData<int, int> RoundTripCases()
    {
        var data = new TheoryData<int, int>();
        var seen = new HashSet<(int Length, int Size)>();
        foreach (var size in new[] { 1, 16, 1024, 512 * 1024 })
        {
            foreach (var length in new[] { 0, 1, size - 1, size, size + 1, 3 * size, 100_000 })
            {
                if (length >= 0 && seen.Add((length, size)))
                {
                    data.Add(length, size);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void SplitThenReassemble_RestoresOriginal(int length, int size)
    {
        var payload = Bytes(length);

        var restored = EbicsSegmentation.Reassemble(EbicsSegmentation.Split(payload, size).Segments);

        restored.Should().Equal(payload);
    }

    [Fact]
    public void CompressSplitReassembleDecompress_RoundTrip()
    {
        // End-to-end: the pipeline direction #32/#33 will compose. A small segment size forces
        // multiple segments without needing megabyte-sized input.
        var data = Bytes(1000);

        var compressed = EbicsCompression.Compress(data);
        var segmented = EbicsSegmentation.Split(compressed, 64);
        var reassembled = EbicsSegmentation.Reassemble(segmented.Segments);
        var restored = EbicsCompression.Decompress(reassembled);

        segmented.NumSegments.Should().BeGreaterThan(1);
        restored.Should().Equal(data);
    }
}
