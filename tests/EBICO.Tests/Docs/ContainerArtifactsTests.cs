using AwesomeAssertions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the container packaging artifacts (issue #61): the <c>Dockerfile</c>,
/// <c>.dockerignore</c> and <c>docker-compose.yml</c> at the repository root, the deployment
/// documentation (<c>docs/deployment/container.md</c>) and its wiring into the doc index and ADR log.
/// They keep the committed artifacts present and coherent so the container image cannot silently
/// lose its key building blocks (base images, the <c>PROJECT</c> build-arg, the compose services) or
/// drop out of the documentation.
/// </summary>
public class ContainerArtifactsTests
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
        File.Exists(path).Should().BeTrue($"the artifact '{Path.Combine(relativeSegments)}' (issue #61) must exist");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Dockerfile_UsesMultiStageBuildWithTheExpectedBaseImagesAndProjectArg()
    {
        var dockerfile = ReadRepoFile("Dockerfile");

        dockerfile.Should().Contain("mcr.microsoft.com/dotnet/sdk:", "the build stage uses the .NET SDK image");
        dockerfile.Should().Contain("mcr.microsoft.com/dotnet/aspnet:", "the runtime stage uses the ASP.NET Core image");
        dockerfile.Should().Contain("ARG PROJECT", "the Dockerfile is parameterised by the project to publish");
        dockerfile.Should().Contain("EBICO.Server.dll", "the default image starts the server");
    }

    [Fact]
    public void DockerIgnore_ExcludesBuildOutput()
    {
        var dockerignore = ReadRepoFile(".dockerignore");

        dockerignore.Should().Contain("bin/");
        dockerignore.Should().Contain("obj/");
    }

    [Fact]
    public void Compose_DefinesServerAndSuiteServices()
    {
        var compose = ReadRepoFile("docker-compose.yml");

        compose.Should().Contain("server:", "the compose stack runs the EBICS server");
        compose.Should().Contain("suite:", "the compose stack runs the Blazor Suite");
        compose.Should().Contain("PROJECT: EBICO.Server", "the server service is built from the parameterised Dockerfile");
        compose.Should().Contain("PROJECT: EBICO.Suite", "the suite service is built from the parameterised Dockerfile");
        compose.Should().Contain("EBICO.Suite.dll", "the suite service overrides the default start command");
    }

    [Theory]
    [InlineData("## Zweck")]
    [InlineData("## Images & Build")]
    [InlineData("## Konfiguration via ENV")]
    [InlineData("## Sicherheit")]
    [InlineData("## Health")]
    [InlineData("## Tests")]
    public void ContainerDoc_HasRequiredSections(string heading)
    {
        var doc = ReadRepoFile("docs", "deployment", "container.md");

        doc.Should().Contain(heading);
    }

    [Fact]
    public void ContainerDoc_DocumentsTheEbicoEnvConfiguration()
    {
        var doc = ReadRepoFile("docs", "deployment", "container.md");

        doc.Should().Contain("Ebico__EndpointPath", "the doc shows how the emulator options bind from ENV");
    }

    [Fact]
    public void DocIndex_LinksTheContainerDoc()
    {
        var index = ReadRepoFile("docs", "index.md");

        index.Should().Contain("deployment/container.md", "every doc page must be linked from the index (Docs-as-Code)");
    }

    [Fact]
    public void Adr0022_ExistsAndIsListedInTheAdrIndex()
    {
        ReadRepoFile("docs", "adr", "0022-container-image-und-konfiguration.md")
            .Should().Contain("# 0022 —", "the ADR follows the MADR heading convention");

        var adrIndex = ReadRepoFile("docs", "adr", "README.md");
        adrIndex.Should().Contain("0022-container-image-und-konfiguration.md", "the ADR must be listed in the index table");
    }
}
