using System.IO.Compression;

namespace EBICO.Core.Statements;

/// <summary>
/// Wraps a single statement document (MT text or camt XML) in a deterministic ZIP container, matching the
/// BTF <c>Container=Zip</c> declaration for the statement/report order types (issue #40). The download
/// engine applies its own transport compression (zlib) and E002 encryption on top of the returned bytes.
/// </summary>
/// <remarks>
/// Determinism is deliberate: <see cref="ZipArchiveEntry.LastWriteTime"/> defaults to the wall clock, which
/// would make the ZIP bytes (and therefore any golden test) change every run. It is set to a caller-supplied
/// fixed timestamp instead, and the compression level is pinned, so identical input yields identical output.
/// </remarks>
public static class StatementZipContainer
{
    /// <summary>
    /// Wraps <paramref name="content"/> as a single ZIP entry named <paramref name="entryName"/>.
    /// </summary>
    /// <param name="entryName">The entry (file) name inside the archive, e.g. <c>C53-20260714.xml</c>.</param>
    /// <param name="content">The raw document bytes to store.</param>
    /// <param name="entryTimestamp">The fixed last-write time stamped on the entry (must be ≥ 1980 for the ZIP DOS-time field).</param>
    /// <returns>The ZIP archive as bytes.</returns>
    /// <exception cref="ArgumentException"><paramref name="entryName"/> is null/empty/whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public static byte[] Wrap(string entryName, byte[] content, DateTimeOffset entryTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = entryTimestamp;
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        return buffer.ToArray();
    }
}
