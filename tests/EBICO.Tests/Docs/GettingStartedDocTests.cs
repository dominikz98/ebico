using AwesomeAssertions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the getting-started / quickstart documentation (issue #63): the "In 5 Minuten zum
/// laufenden Emulator"-page (<c>docs/getting-started.md</c>), its wiring into the doc index and the
/// root README. They keep the consumer-facing entry point present and coherent — the two ways to start
/// the emulator, the connector sample, the multi-version hint (H003/H004/H005) and the schema/license
/// note — so it cannot silently lose a building block or drop out of the index.
/// </summary>
public class GettingStartedDocTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EBICO.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (no EBICO.sln found walking up from "
            + $"'{AppContext.BaseDirectory}').");
    }

    private static string ReadRepoFile(params string[] relativeSegments)
    {
        var path = Path.Combine(new[] { RepoRoot() }.Concat(relativeSegments).ToArray());
        File.Exists(path).Should().BeTrue($"the artifact '{Path.Combine(relativeSegments)}' (issue #63) must exist");
        return File.ReadAllText(path);
    }

    private static string GettingStartedDoc() => ReadRepoFile("docs", "getting-started.md");

    [Theory]
    [InlineData("## 1. Emulator starten")]
    [InlineData("## 2. Client ausprobieren (Quickstart-Sample)")]
    [InlineData("## Andere EBICS-Versionen (H003 / H004 / H005)")]
    [InlineData("## Schemas & Lizenz")]
    public void GettingStartedDoc_HasRequiredSections(string heading)
    {
        GettingStartedDoc().Should().Contain(heading);
    }

    [Fact]
    public void GettingStartedDoc_ShowsBothWaysToStartTheEmulator()
    {
        var doc = GettingStartedDoc();

        doc.Should().Contain("docker compose up", "the Docker path is documented");
        doc.Should().Contain("dotnet run --project src/EBICO.Server", "the dotnet-run path is documented");
        doc.Should().Contain("/health", "the doc shows how to verify liveness");
    }

    [Fact]
    public void GettingStartedDoc_PointsAtTheConnectorSample()
    {
        GettingStartedDoc()
            .Should().Contain("samples/EBICO.Connector.Quickstart", "the doc points at the runnable sample");
    }

    [Fact]
    public void GettingStartedDoc_MentionsAllSupportedVersions()
    {
        var doc = GettingStartedDoc();

        doc.Should().Contain("H003");
        doc.Should().Contain("H004");
        doc.Should().Contain("H005");
    }

    [Fact]
    public void GettingStartedDoc_KeepsTheSchemaAndLicenseNote()
    {
        var doc = GettingStartedDoc();

        doc.Should().Contain("ebics.org", "the doc points to the official schema/license source");
        doc.Should().Contain("MIT", "the doc states the code license");
    }

    [Fact]
    public void DocIndex_LinksTheGettingStartedDoc()
    {
        ReadRepoFile("docs", "index.md")
            .Should().Contain("getting-started.md", "every doc page must be linked from the index (Docs-as-Code)");
    }

    [Fact]
    public void RootReadme_LinksTheGettingStartedDoc()
    {
        ReadRepoFile("README.md")
            .Should().Contain("docs/getting-started.md", "the root README points newcomers at the quickstart");
    }
}
