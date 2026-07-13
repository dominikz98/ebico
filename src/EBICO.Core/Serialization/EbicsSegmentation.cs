namespace EBICO.Core.Serialization;

/// <summary>
/// Splits EBICS order data into transport segments and reassembles them. On the wire the order data
/// of every request/response is carried as <c>base64(compress(orderDataXml))</c> (or, for secured
/// transactions, <c>base64(encrypt(compress(orderDataXml)))</c>); once that byte stream exceeds a
/// single segment it is delivered across several messages, one <c>DataTransfer/OrderData</c> element
/// each. This helper is the <i>split</i> / <i>reassemble</i> step that sits on top of
/// <see cref="EbicsCompression"/> (and, for secured orders,
/// <see cref="EBICO.Core.Crypto.EncryptionE002"/>); the base64 layer is handled per segment by the
/// <c>base64Binary</c> bindings.
/// </summary>
/// <remarks>
/// <para>
/// The primitive is deliberately pure and policy-free. <see cref="Split"/> is a deterministic byte
/// splitter — the same payload and size always produce byte-identical segments — and enforces no
/// maximum segment count or order-data size. That enforcement, together with the transaction id, the
/// phase handling (Initialisation/Transfer/Receipt) and the
/// <c>NumSegments</c>/<c>SegmentNumber</c>/<c>lastSegment</c> header mapping, is the transaction
/// engine's job (M4 upload/download, issues #32/#33). <see cref="Reassemble"/> concatenates its
/// input in list order and neither sorts by <c>SegmentNumber</c> nor detects gaps or duplicates:
/// sequence integrity is the transaction engine's responsibility, which builds the ordered list
/// before calling in.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the configured segment size is measured in <i>raw</i> (pre-base64)
/// bytes; the base64 wire size is roughly 4/3 of it. Whether EBICS applies its ~1&#160;MB segment
/// ceiling to the raw or the base64-encoded size — and whether the segments are portions of one
/// shared base64 stream rather than an independently base64-encoded <c>byte[]</c> per segment as the
/// <c>base64Binary</c> bindings model them — is not yet verified against the official EBICS Annex
/// (the specs are proprietary and not in the repo — see <c>CLAUDE.md</c>). The choice is confined to
/// the size parameter (and <c>EbicoServerOptions.SegmentSizeBytes</c>); split → reassemble
/// round-trips hold regardless.
/// </para>
/// </remarks>
public static class EbicsSegmentation
{
    /// <summary>
    /// Splits <paramref name="payload"/> into ordered raw-byte segments of at most
    /// <paramref name="maxSegmentSizeBytes"/> bytes each.
    /// </summary>
    /// <param name="payload">The raw bytes to segment (e.g. the compressed/encrypted order data).</param>
    /// <param name="maxSegmentSizeBytes">The maximum size of a single segment in bytes; must be positive.</param>
    /// <returns>
    /// The segments in transmission order. Always contains at least one segment: an empty
    /// <paramref name="payload"/> yields a single empty segment (EBICS transports at least one
    /// <c>OrderData</c> element, so <c>NumSegments</c> &#8805; 1).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSegmentSizeBytes"/> is not positive.</exception>
    public static SegmentedOrderData Split(ReadOnlySpan<byte> payload, int maxSegmentSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSegmentSizeBytes);

        if (payload.IsEmpty)
        {
            return new SegmentedOrderData([[]]);
        }

        // Overflow-safe ceil division (payload is non-empty here): a naive
        // (Length + size - 1) / size overflows int for a large maxSegmentSizeBytes.
        var count = (payload.Length - 1) / maxSegmentSizeBytes + 1;
        var segments = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            var offset = i * maxSegmentSizeBytes;
            var length = Math.Min(maxSegmentSizeBytes, payload.Length - offset);
            segments[i] = payload.Slice(offset, length).ToArray();
        }

        return new SegmentedOrderData(segments);
    }

    /// <summary>Reverses <see cref="Split"/> by concatenating <paramref name="segments"/> in list order.</summary>
    /// <param name="segments">
    /// The segments in transmission order (as produced by <see cref="Split"/>). Must contain at least
    /// one segment; individual segments may be empty.
    /// </param>
    /// <returns>The reassembled payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> or one of its elements is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="segments"/> is empty.</exception>
    public static byte[] Reassemble(IReadOnlyList<byte[]> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            throw new ArgumentException("At least one segment is required.", nameof(segments));
        }

        var total = 0;
        foreach (var segment in segments)
        {
            ArgumentNullException.ThrowIfNull(segment);
            total += segment.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var segment in segments)
        {
            segment.CopyTo(result, offset);
            offset += segment.Length;
        }

        return result;
    }
}

/// <summary>The result of <see cref="EbicsSegmentation.Split"/>: the ordered order-data segments.</summary>
/// <param name="Segments">
/// The raw-byte segments in transmission order; always at least one (possibly empty) segment. Each
/// element is carried as one <c>DataTransfer/OrderData</c> value on the wire.
/// </param>
public readonly record struct SegmentedOrderData(IReadOnlyList<byte[]> Segments)
{
    /// <summary>The number of segments (the EBICS <c>NumSegments</c> header value); always &#8805; 1.</summary>
    public int NumSegments => Segments.Count;
}
