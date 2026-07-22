using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the branch-protection rule on <c>main</c> (issue #3, ADR-0028). The rule itself lives
/// in the GitHub repository settings, not in the repository content — it is not versioned, shows up in no
/// diff and cannot be asserted from a unit test. What <em>can</em> be kept honest is the documented
/// target state: the list of required status checks in <c>docs/development/ci.md</c> must match the job
/// display names in <c>.github/workflows/ci.yml</c> exactly. Renaming a CI job otherwise breaks the gate
/// silently — the configured check never reports again and every PR hangs on "Expected".
/// </summary>
public class BranchProtectionDocTests
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
        File.Exists(path).Should().BeTrue($"the artifact '{Path.Combine(relativeSegments)}' (issue #3) must exist");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Job display names (<c>name:</c>) of a workflow — that string, not the YAML key, is what GitHub
    /// reports as the status-check context and what a protection rule has to name.
    /// </summary>
    private static IReadOnlyList<string> JobDisplayNames(string workflowFileName)
    {
        var workflow = ReadRepoFile(".github", "workflows", workflowFileName);

        // A job is a two-space-indented key followed by its four-space-indented display name.
        return Regex.Matches(workflow, @"^  [A-Za-z0-9_-]+:\r?\n    name: (?<name>.+?)\r?$", RegexOptions.Multiline)
            .Select(match => match.Groups["name"].Value.Trim())
            .ToList();
    }

    /// <summary>Required checks listed between the marker comments in <c>ci.md</c>.</summary>
    private static IReadOnlyList<string> DocumentedRequiredChecks()
    {
        var ci = ReadRepoFile("docs", "development", "ci.md");

        var block = Regex.Match(ci, @"<!-- required-checks:start -->(?<body>.*?)<!-- required-checks:end -->",
            RegexOptions.Singleline);
        block.Success.Should().BeTrue(
            "ci.md must delimit the required-status-check list with the required-checks markers so this guard can read it");

        return Regex.Matches(block.Groups["body"].Value, @"^- `(?<check>[^`]+)`\r?$", RegexOptions.Multiline)
            .Select(match => match.Groups["check"].Value)
            .ToList();
    }

    [Fact]
    public void DocumentedRequiredChecks_MatchTheCiWorkflowJobsExactly()
    {
        var jobs = JobDisplayNames("ci.yml");

        jobs.Should().NotBeEmpty("ci.yml must define jobs with display names");
        DocumentedRequiredChecks().Should().BeEquivalentTo(
            jobs,
            "every ci.yml job is a required check and every documented check must exist — a renamed, added "
            + "or removed job has to be pulled through to ci.md and to the repo setting");
    }

    [Fact]
    public void DocumentedRequiredChecks_ExcludeTheTagTriggeredReleaseJob()
    {
        var releaseJobs = JobDisplayNames("release.yml");
        var documented = DocumentedRequiredChecks();

        releaseJobs.Should().NotBeEmpty("release.yml must define its publish job");
        documented.Should().NotIntersectWith(
            releaseJobs,
            "release.yml only fires on v*.*.* tags — as a required check it would never report on a pull "
            + "request and would block every merge permanently (ADR-0028)");
    }

    [Fact]
    public void CiDoc_DocumentsTheProtectionSettingsAndTheirRationale()
    {
        var ci = ReadRepoFile("docs", "development", "ci.md");

        ci.Should().Contain("## Branch-Protection", "the target state of the unversioned repo setting must be documented");
        ci.Should().Contain("enforce_admins", "the admin binding is the decisive setting in a solo repo");
        ci.Should().Contain("strict", "required checks are configured strict (branch must be up to date)");
        ci.Should().Contain("0028-branch-protection-main.md", "the section must reference its ADR");
    }

    [Fact]
    public void Adr0028_ExistsAndIsListedInTheAdrIndex()
    {
        ReadRepoFile("docs", "adr", "0028-branch-protection-main.md")
            .Should().Contain("# 0028 —", "the ADR follows the MADR heading convention");

        ReadRepoFile("docs", "adr", "README.md")
            .Should().Contain("0028-branch-protection-main.md", "the ADR must be listed in the index table");
    }
}
