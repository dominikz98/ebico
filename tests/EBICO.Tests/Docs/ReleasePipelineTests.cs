using AwesomeAssertions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the release/publish pipeline (issue #62): the tag-triggered workflow
/// (<c>.github/workflows/release.yml</c>), its ADR (<c>docs/adr/0027-*.md</c>) and the release runbook
/// (<c>docs/development/release.md</c>). The real registry push cannot be exercised in a unit test
/// (it needs the <c>NUGET_API_KEY</c> secret and GHCR credentials that only exist in GitHub Actions),
/// so these assert the workflow keeps its key building blocks — the tag trigger, version-from-tag,
/// the nuget.org push, the GHCR container push and the auto-generated GitHub release — and stays wired
/// into the documentation and the ADR log.
/// </summary>
public class ReleasePipelineTests
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
        File.Exists(path).Should().BeTrue($"the artifact '{Path.Combine(relativeSegments)}' (issue #62) must exist");
        return File.ReadAllText(path);
    }

    private static string ReleaseWorkflow() => ReadRepoFile(".github", "workflows", "release.yml");

    [Fact]
    public void ReleaseWorkflow_TriggersOnCalVerTagsOnly()
    {
        var workflow = ReleaseWorkflow();

        workflow.Should().Contain("tags:", "the release is tag-triggered, not run on every push");
        workflow.Should().Contain("v*.*.*", "only CalVer-shaped tags start a release");
    }

    [Fact]
    public void ReleaseWorkflow_DerivesTheVersionFromTheTag()
    {
        var workflow = ReleaseWorkflow();

        workflow.Should().Contain("GITHUB_REF_NAME", "the package/image version is derived from the tag");
        workflow.Should().Contain("-p:Version=", "the tag version overrides the date-based CalVer computation");
    }

    [Fact]
    public void ReleaseWorkflow_PacksBothPublishedLibraries()
    {
        var workflow = ReleaseWorkflow();

        workflow.Should().Contain("EBICO.Core/EBICO.Core.csproj", "Core is packed for the release");
        workflow.Should().Contain("EBICO.Connector/EBICO.Connector.csproj", "Connector is packed for the release");
    }

    [Fact]
    public void ReleaseWorkflow_PushesToNugetOrgWithTheApiKeySecret()
    {
        var workflow = ReleaseWorkflow();

        workflow.Should().Contain("dotnet nuget push", "the packages are pushed to a feed");
        workflow.Should().Contain("api.nuget.org", "the publish target is nuget.org (ADR-0027)");
        workflow.Should().Contain("secrets.NUGET_API_KEY", "the push authenticates with the NUGET_API_KEY secret");
        workflow.Should().Contain("--skip-duplicate", "re-runs must be idempotent");
    }

    [Fact]
    public void ReleaseWorkflow_PushesTheServerImageToGhcr()
    {
        var workflow = ReleaseWorkflow();

        workflow.Should().Contain("docker/login-action", "the container push logs in to a registry");
        workflow.Should().Contain("ghcr.io", "the container image is published to GHCR (ADR-0022/0027)");
        workflow.Should().Contain("PROJECT=EBICO.Server", "the pushed image is the server image");
        workflow.Should().Contain("packages: write", "GHCR push needs the packages permission");
    }

    [Fact]
    public void ReleaseWorkflow_CreatesAGithubReleaseWithGeneratedNotes()
    {
        var workflow = ReleaseWorkflow();

        workflow.Should().Contain("gh release create", "a GitHub release is created for every tag");
        workflow.Should().Contain("--generate-notes", "release notes are generated automatically");
    }

    [Fact]
    public void Adr0027_ExistsAndIsListedInTheAdrIndex()
    {
        ReadRepoFile("docs", "adr", "0027-nuget-publish-und-release-pipeline.md")
            .Should().Contain("# 0027 —", "the ADR follows the MADR heading convention");

        var adrIndex = ReadRepoFile("docs", "adr", "README.md");
        adrIndex.Should().Contain("0027-nuget-publish-und-release-pipeline.md", "the ADR must be listed in the index table");
    }

    [Fact]
    public void ReleaseRunbook_ExistsAndIsLinkedFromTheDocIndex()
    {
        ReadRepoFile("docs", "development", "release.md")
            .Should().Contain("# Release-Runbook", "the runbook documents how to cut a release");

        var index = ReadRepoFile("docs", "index.md");
        index.Should().Contain("development/release.md", "every doc page must be linked from the index (Docs-as-Code)");
    }
}
