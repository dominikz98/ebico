using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the English naming of files, doc slugs and Suite routes (issue #134, epic #128).
/// The prose translation is visible in every diff; the <em>names</em> are not — a German slug or route
/// only shows up as a broken link somewhere else, and the CI link checker (lychee, relative links in
/// <c>*.md</c>) sees neither the non-markdown references (XML doc, <c>Directory.*.props</c>, the
/// absolute GitHub URL in <c>DemoDataBanner.razor</c>) nor a file that was renamed without being
/// re-registered in the ADR index. These tests make the naming rule from <c>CLAUDE.md</c> executable so
/// the renames of #134 cannot silently rot back.
/// </summary>
public class DocumentationNamingTests
{
    /// <summary>
    /// German tokens that must not appear in a tracked file name, doc slug or route. Deliberately
    /// spelling-based rather than dictionary-based: it only has to catch the vocabulary this repository
    /// actually used before #134 plus the obvious neighbours.
    /// </summary>
    private static readonly string[] GermanNameTokens =
    [
        "stammdaten", "schluessel", "transaktion", "teilnehmer", "kunde", "zahlung", "berechtigung",
        "krypto", "bibliothek", "katalog", "strategie", "verwaltung", "verifikation", "validierung",
        "versionierung", "konfiguration", "ereignis", "protokollspeicher", "kontoauszug",
        "konformitaet", "anbindung", "aenderung", "verschluesselung", "uebersicht", "pruefung",
        "sicherheit", "entscheidung", "anleitung", "generierte", "committen", "domaenen",
        "proprietaeren", "serverseitige", "clientseitige", "-und-", "-ohne-", "-mit-",
    ];

    /// <summary>The Suite's routable pages with the route each one has to declare.</summary>
    public static TheoryData<string, string> SuiteRoutes() => new()
    {
        { "MasterData.razor", "/master-data" },
        { "Transactions.razor", "/transactions" },
        { "Keys.razor", "/keys" },
    };

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
        File.Exists(path).Should().BeTrue($"the artifact '{Path.Combine(relativeSegments)}' (issue #134) must exist");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// File names below <paramref name="relativeSegments"/>, recursively, relative to the repository
    /// root and with forward slashes so the assertion messages read like the repository paths.
    /// </summary>
    private static IReadOnlyList<string> RepoFiles(params string[] relativeSegments)
    {
        var root = RepoRoot();
        var directory = Path.Combine(new[] { root }.Concat(relativeSegments).ToArray());

        return [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            // bin/obj hold build output, not authored files; the generated XSD bindings are out of
            // scope by ADR-0006 (their German documentation comes from the proprietary schemas).
            .Where(path => !path.Contains("/bin/") && !path.Contains("/obj/"))
            .Where(path => !path.StartsWith("src/EBICO.Core/Schema/", StringComparison.Ordinal))];
    }

    /// <summary>ADR files on disk (<c>docs/adr/NNNN-*.md</c>), file name only, ordered by number.</summary>
    private static IReadOnlyList<string> AdrFilesOnDisk()
        => [.. Directory.EnumerateFiles(Path.Combine(RepoRoot(), "docs", "adr"), "*.md")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => Regex.IsMatch(name, @"^\d{4}-"))
            .Order(StringComparer.Ordinal)];

    /// <summary>ADR file names linked from the index table in <c>docs/adr/README.md</c>.</summary>
    private static IReadOnlyList<string> AdrFilesInIndex()
        => [.. Regex.Matches(
                ReadRepoFile("docs", "adr", "README.md"),
                @"^\| \[\d{4}\]\((?<file>\d{4}-[^)]+\.md)\)",
                RegexOptions.Multiline)
            .Select(match => match.Groups["file"].Value)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void AdrIndex_ListsExactlyTheAdrsOnDisk()
    {
        var onDisk = AdrFilesOnDisk();

        onDisk.Should().NotBeEmpty("docs/adr/ must hold the numbered ADRs");
        AdrFilesInIndex().Should().Equal(
            onDisk,
            "the index table in docs/adr/README.md is the entry point to the ADRs — a renamed, added or "
            + "removed ADR has to be pulled through, otherwise the index links into the void (#134)");
    }

    [Fact]
    public void EveryAdr_UsesTheNumberedMadrHeading()
    {
        foreach (var file in AdrFilesOnDisk())
        {
            var number = file[..4];

            ReadRepoFile("docs", "adr", file).Should().StartWith(
                $"# {number} —",
                $"'{file}' must open with the MADR heading convention '# {number} — <title>' so heading and "
                + "slug stay in step");
        }
    }

    [Theory]
    [MemberData(nameof(SuiteRoutes))]
    public void SuitePage_DeclaresItsEnglishRoute(string pageFileName, string expectedRoute)
    {
        var page = ReadRepoFile("src", "EBICO.Suite", "Components", "Pages", pageFileName);

        page.Should().StartWith(
            $"@page \"{expectedRoute}\"",
            $"'{pageFileName}' is reached under {expectedRoute} — the route is part of the public URL surface "
            + "and is documented in docs/suite/ui-shell.md (#134)");
    }

    [Fact]
    public void NavMenu_LinksExactlyTheEnglishRoutes()
    {
        var navMenu = ReadRepoFile("src", "EBICO.Suite", "Components", "Layout", "NavMenu.razor");

        var hrefs = Regex.Matches(navMenu, @"<NavLink[^>]*?href=""(?<href>[^""]*)""")
            .Select(match => match.Groups["href"].Value)
            .ToList();

        hrefs.Should().Equal(
            ["", "master-data", "transactions", "keys"],
            "the navigation is the only way into the pages — a route rename that misses NavMenu leaves "
            + "dead links behind (NavMenuTests asserts the same set on the rendered markup)");
    }

    [Fact]
    public void NoTrackedFileName_ContainsAGermanToken()
    {
        var offenders = RepoFiles()
            .Where(path => GermanNameTokens.Any(token =>
                path.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        offenders.Should().BeEmpty(
            "the project language is English including file names, doc slugs and folder names (CLAUDE.md, "
            + "#133/#134) — generated XSD bindings (ADR-0006) and build output are excluded");
    }

    private static IReadOnlyList<string> RepoFiles()
        => [.. RepoFiles("docs"), .. RepoFiles("src"), .. RepoFiles("tests"), .. RepoFiles(".claude")];
}
