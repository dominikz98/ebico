using System.IO.Compression;

namespace EBICO.Core.Serialization;

/// <summary>
/// Compresses and decompresses EBICS order data. On the wire the order data of every request/response
/// is carried as <c>base64(compress(orderDataXml))</c>; this helper is the <i>compress</i> step (the
/// base64 layer is handled by the <c>base64Binary</c> bindings, the XML by
/// <see cref="EbicsXmlSerializer"/>). The onboarding flows (INI/HIA/HPB) are the first users; the
/// later transaction pipeline (segmentation &amp; base64) builds on this same primitive rather than
/// replacing it.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> EBICS specifies the order data as "ZIP-compressed". In practice this is
/// the zlib data stream (RFC 1950, a DEFLATE stream wrapped with a 2-byte header and an Adler-32
/// checksum) as produced by Java's <c>Deflater</c> defaults, which <see cref="ZLibStream"/> is
/// byte-compatible with. The exact framing (zlib vs. raw DEFLATE vs. gzip) is not yet verified
/// against the official EBICS Annex (the specs are proprietary and not in the repo — see
/// <c>CLAUDE.md</c>). The choice is confined to the single <see cref="Wrap"/> seam, so switching it
/// for real-bank interop is a one-line change; self-consistent compress → decompress round-trips
/// hold regardless.
/// </remarks>
public static class EbicsCompression
{
    private const CompressionLevel Level = CompressionLevel.Optimal;

    /// <summary>Compresses <paramref name="data"/> for embedding in an EBICS order-data element.</summary>
    /// <param name="data">The raw (uncompressed) order-data bytes.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var compressor = Wrap(output, compress: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Reverses <see cref="Compress"/>.</summary>
    /// <param name="data">The compressed bytes (as received on the wire, after base64-decoding).</param>
    /// <returns>The original, decompressed bytes.</returns>
    /// <exception cref="InvalidDataException">The data is not in the expected compressed format.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var decompressor = Wrap(input, compress: false);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    // The single seam that decides the compression framing (see the Spec-Vorbehalt above).
    private static Stream Wrap(Stream inner, bool compress) => compress
        ? new ZLibStream(inner, Level, leaveOpen: true)
        : new ZLibStream(inner, CompressionMode.Decompress, leaveOpen: true);
}
