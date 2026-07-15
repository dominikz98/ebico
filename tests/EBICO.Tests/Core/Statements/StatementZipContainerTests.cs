using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>Tests for <see cref="StatementZipContainer"/> (issue #40): readable ZIP output and determinism.</summary>
public class StatementZipContainerTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Wrap_ProducesReadableZip_WithNamedEntry()
    {
        var content = Encoding.UTF8.GetBytes("<doc/>");

        var zip = StatementZipContainer.Wrap("C53-20260731.xml", content, Timestamp);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = archive.GetEntry("C53-20260731.xml");
        entry.Should().NotBeNull();
        using var reader = new StreamReader(entry!.Open());
        reader.ReadToEnd().Should().Be("<doc/>");
    }

    [Fact]
    public void Wrap_SameInputs_IsDeterministic()
    {
        var content = Encoding.UTF8.GetBytes("<doc/>");

        StatementZipContainer.Wrap("a.xml", content, Timestamp)
            .Should().Equal(StatementZipContainer.Wrap("a.xml", content, Timestamp));
    }
}
