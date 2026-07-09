using System.Text;
using AwesomeAssertions;
using EBICO.Core.Serialization;

namespace EBICO.Tests.Serialization;

/// <summary>Tests for the EBICS order-data compression helper (issue #47).</summary>
public class EbicsCompressionTests
{
    [Fact]
    public void RoundTrip_Text_RestoresOriginal()
    {
        var data = Encoding.UTF8.GetBytes("<SignaturePubKeyOrderData>abc</SignaturePubKeyOrderData>");

        var restored = EbicsCompression.Decompress(EbicsCompression.Compress(data));

        restored.Should().Equal(data);
    }

    [Fact]
    public void RoundTrip_Empty_RestoresEmpty()
    {
        var restored = EbicsCompression.Decompress(EbicsCompression.Compress([]));

        restored.Should().BeEmpty();
    }

    [Fact]
    public void RoundTrip_Large_RestoresOriginal()
    {
        var data = new byte[100_000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 31 % 251);
        }

        var restored = EbicsCompression.Decompress(EbicsCompression.Compress(data));

        restored.Should().Equal(data);
    }

    [Fact]
    public void Compress_RepetitiveData_IsSmallerThanInput()
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 10_000));

        var compressed = EbicsCompression.Compress(data);

        compressed.Length.Should().BeLessThan(data.Length);
    }

    [Fact]
    public void Compress_ProducesZlibHeader()
    {
        // Pins the compression framing seam (RFC 1950 zlib): the first byte is the CMF 0x78.
        var compressed = EbicsCompression.Compress(Encoding.UTF8.GetBytes("data"));

        compressed[0].Should().Be(0x78);
    }

    [Fact]
    public void Decompress_Garbage_Throws()
    {
        var act = () => EbicsCompression.Decompress([1, 2, 3, 4]);

        act.Should().Throw<InvalidDataException>();
    }
}
