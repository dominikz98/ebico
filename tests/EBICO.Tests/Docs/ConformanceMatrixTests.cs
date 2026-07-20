using AwesomeAssertions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the real-client conformance page (<c>docs/development/conformance-real-clients.md</c>,
/// issue #59). They keep the hand-written page in sync with the committed vendor corpus: every client that
/// has captures under <c>Conformance/Vendor/</c> must appear in the compatibility matrix, and the page must
/// carry its required sections — so a new vendor capture cannot silently land without documentation.
/// </summary>
public class ConformanceMatrixTests
{
    private static readonly string PageText = File.ReadAllText(ConformancePagePath());

    [Theory]
    [InlineData("## Kompatibilitätsmatrix")]
    [InlineData("## Abweichungen")]
    [InlineData("## Capture-Anleitung")]
    public void Page_HasRequiredSections(string heading)
    {
        PageText.Should().Contain(heading);
    }

    [Fact]
    public void CompatibilityMatrix_NamesEveryCommittedVendorClient()
    {
        var clients = CommittedVendorClients();
        // Vacuously true on a checkout without the corpus copied to the output; meaningful once captures
        // are present (as they are when the repo's committed corpus is built).
        foreach (var client in clients)
        {
            PageText.Should().Contain(
                client,
                $"the compatibility matrix must document the committed vendor client '{client}'");
        }
    }

    private static IReadOnlyList<string> CommittedVendorClients()
    {
        var root = Conformance.VendorCaptureCorpus.CorpusRoot;
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().ToList()
            : [];
    }

    private static string ConformancePagePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EBICO.sln")))
            {
                return Path.Combine(directory.FullName, "docs", "development", "conformance-real-clients.md");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (no EBICO.sln found walking up from "
            + $"'{AppContext.BaseDirectory}').");
    }
}
