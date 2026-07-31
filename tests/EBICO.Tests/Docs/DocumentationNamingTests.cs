using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the English project language in <em>names</em> and in <em>prose</em> (issues #134
/// and #141, epic #128). A translated sentence is visible in every diff; a German name is not — it only
/// shows up as a broken link somewhere else, and the CI link checker (lychee, relative links in
/// <c>*.md</c>) sees neither the non-markdown references (XML doc, <c>Directory.*.props</c>, the
/// absolute GitHub URL in <c>DemoDataBanner.razor</c>) nor a file that was renamed without being
/// re-registered in the ADR index. Nor does anything otherwise notice a single German sentence left
/// behind in an XML-doc block that ships inside the published NuGet package — which is exactly what
/// #141 found in <c>EbicsSegmentation</c>. These tests make the rule from <c>CLAUDE.md</c> executable.
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

    /// <summary>
    /// German words that must not appear in the <em>content</em> of a tracked source or doc file.
    /// Kept deliberately <b>narrow</b>: it only holds words that cannot occur in English prose. The
    /// obvious additions are traps — <c>der</c> collides with DER encoding, whose name litters the
    /// PKCS#8 / SubjectPublicKeyInfo comments, and <c>die</c>, <c>man</c> and <c>links</c> are ordinary
    /// English words. Before extending the list, run the candidate over the repository and read the hit
    /// list; a guard that cries wolf gets silenced rather than heeded.
    /// </summary>
    /// <remarks>
    /// The singular <c>Kunde</c> is deliberately absent. German <em>test data</em> is legitimate — this is
    /// an emulator for German banks, so fixtures carry names like <c>"Kunde AG"</c> or
    /// <c>"Stadtwerke Musterstadt"</c> and MT940 booking texts are German by construction. Including it
    /// would mean allowlisting five test files plus a doc page, which would blunt the guard for the
    /// <em>prose</em> it exists to police. A future German gloss spelled <c>(Kunde)</c> would therefore
    /// slip through; that is the accepted trade-off.
    /// </remarks>
    private static readonly string[] GermanProseWords =
    [
        "nicht", "werden", "muss", "müssen", "keine", "damit", "jedoch", "bereits", "sowie",
        "während", "Teilnehmer", "Kunden", "Stammdaten", "Berechtigung", "Validierung",
        "Verschluesselung", "Schluessel", "Zustand", "Beispiel", "Obergrenze",
        // "Spec-Vorbehalt" was the marker term for an unverified spec assumption in 89 places across
        // 57 files — the single largest German remnant #141 found. It is "spec caveat" now (CLAUDE.md).
        "Vorbehalt", "Vorbehalte", "Testdaten",
        // German abbreviations read as noise in English prose and are easy to type by reflex.
        "vgl", "bzw", "ggf", "Kleinbuchstabe", "Grossbuchstabe", "Sperrung",
        // These hid in the `// comment` lines of the C# samples inside the docs, where the eye skips
        // over them because the surrounding code is language-neutral.
        "garantiert", "valide", "gueltig", "ungueltig", "liefert", "lesbare",
    ];

    /// <summary>
    /// Files whose German content is deliberate and documented in <c>CLAUDE.md</c>. Everything here is a
    /// decision, not an oversight — do not extend this list to make a new finding go away.
    /// </summary>
    private static readonly string[] GermanContentAllowlist =
    [
        // The INI/HIA letter is printed and posted to a German-speaking bank.
        "src/EBICO.Connector/Onboarding/Letter/InitializationLetterTextBuilder.cs",
        // Synthetic statement data for a German bank emulator — German company names are the point.
        "src/EBICO.Core/Statements/SyntheticStatementGenerator.cs",
        // This file: the token lists above are its data.
        "tests/EBICO.Tests/Docs/DocumentationNamingTests.cs",
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

    [Fact]
    public void NoTrackedSourceOrDoc_ContainsGermanProse()
    {
        var offenders = new List<string>();
        var pattern = new Regex(
            @"\b(" + string.Join('|', GermanProseWords.Select(Regex.Escape)) + @")\b",
            RegexOptions.IgnoreCase);

        foreach (var relativePath in ProseScanPaths()
            .Where(path => !GermanContentAllowlist.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            var content = StripQuotedIdentifiers(File.ReadAllText(Path.Combine(RepoRoot(), relativePath)));

            offenders.AddRange(pattern.Matches(content)
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(word => $"{relativePath}: '{word}'"));
        }

        offenders.Should().BeEmpty(
            "prose is English too, not only names (CLAUDE.md, #141) — XML doc travels into the published "
            + "NuGet package, so a German sentence there reaches every consumer. Deliberate exceptions "
            + "belong in GermanContentAllowlist with a reason, not in the prose");
    }

    /// <summary>
    /// Removes markdown inline code spans (<c>`…`</c>) and XML-doc <c>&lt;c&gt;</c> elements. Inside
    /// those, a German word <em>names a thing</em> — a former identifier, an old slug, a German fixture
    /// value — rather than being prose, and documentation has to be able to discuss the very terms it
    /// forbids. Fenced code blocks are deliberately <b>not</b> stripped: the <c>// comment</c> lines of
    /// the C# samples in <c>docs/protocol/</c> are exactly where #141 found untranslated German.
    /// </summary>
    private static string StripQuotedIdentifiers(string content)
        => Regex.Replace(Regex.Replace(content, "`[^`\r\n]*`", "``"), @"<c>.*?</c>", "<c/>",
            RegexOptions.Singleline);

    /// <summary>
    /// Files whose content is authored prose or code. Data-ish formats are excluded (sample XML, JSON
    /// fixtures, CSS, JS bundles) — a German word there is payload, not prose. The repository-root
    /// markdown is included explicitly: <c>CLAUDE.md</c> is the most-read contributor document of all.
    /// </summary>
    private static IReadOnlyList<string> ProseScanPaths()
        => [.. RepoFiles().Where(path => ScannedContentExtensions.Contains(Path.GetExtension(path))),
            "CLAUDE.md", "README.md"];

    private static readonly string[] ScannedContentExtensions = [".cs", ".razor", ".md"];

    private static IReadOnlyList<string> RepoFiles()
        => [.. RepoFiles("docs"), .. RepoFiles("src"), .. RepoFiles("tests"), .. RepoFiles(".claude")];
}
