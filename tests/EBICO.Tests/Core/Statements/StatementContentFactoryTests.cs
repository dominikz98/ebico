using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>
/// Tests for <see cref="StatementContentFactory"/> (issue #40): each statement order type produces a
/// readable ZIP whose single entry is in the expected format, and generation is deterministic.
/// </summary>
public class StatementContentFactoryTests
{
    private static readonly DateOnly Start = new(2026, 6, 1);
    private static readonly DateOnly End = new(2026, 6, 30);
    private static readonly DateTimeOffset Created = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("STA", ":20:")]
    [InlineData("VMK", ":34F:")]
    [InlineData("C53", "camt.053.001.08")]
    [InlineData("C52", "camt.052.001.08")]
    [InlineData("C54", "camt.054.001.08")]
    public void Create_ProducesZippedContent_ForEachOrderType(string orderType, string marker)
    {
        var zip = StatementContentFactory.Create(orderType, "EBICOHOST", "PARTNER01", "USER01", Start, End, Created);

        Encoding.UTF8.GetString(Unzip(zip)).Should().Contain(marker);
    }

    [Fact]
    public void Create_SameInputs_IsDeterministic()
    {
        var a = StatementContentFactory.Create("C53", "EBICOHOST", "PARTNER01", "USER01", Start, End, Created);
        var b = StatementContentFactory.Create("C53", "EBICOHOST", "PARTNER01", "USER01", Start, End, Created);

        a.Should().Equal(b);
    }

    [Fact]
    public void Create_UnsupportedOrderType_Throws()
    {
        var act = () => StatementContentFactory.Create("XXX", "EBICOHOST", "PARTNER01", "USER01", Start, End, Created);

        act.Should().Throw<ArgumentException>();
    }

    private static byte[] Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var stream = archive.Entries.Single().Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
