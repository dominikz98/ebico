using System.Reflection;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Core;

namespace EBICO.Tests.Packaging;

/// <summary>
/// Verifies that the published libraries (<c>EBICO.Core</c>, <c>EBICO.Connector</c>) carry the NuGet
/// packaging metadata configured for issue #50: a CalVer informational version and the shared
/// description/company/copyright fields. The actual package contents (README, symbols, license
/// expression, dependency) are validated by the CI pack job; here we assert the metadata is baked into
/// the assemblies at build time.
/// </summary>
public sealed class PackageMetadataTests
{
    [Fact]
    public void Connector_assembly_carries_package_metadata()
        => AssertPackageMetadata(typeof(IEbicsClient).Assembly);

    [Fact]
    public void Core_assembly_carries_package_metadata()
        => AssertPackageMetadata(typeof(EbicsVersion).Assembly);

    private static void AssertPackageMetadata(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        informational.Should().NotBeNull();

        // CalVer {YEAR}.{MONTH}.{BUILD} (ADR-0024); NuGet normalises the month without a leading zero.
        // SourceLink appends "+<commit-sha>" — only the version part before it is relevant.
        var version = informational!.InformationalVersion.Split('+')[0];
        version.Should().MatchRegex(@"^\d{4}\.\d{1,2}\.\d+$");

        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>();
        description.Should().NotBeNull();
        description!.Description.Should().NotBeNullOrWhiteSpace();

        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
        company.Should().NotBeNull();
        company!.Company.Should().Be("tecvia");

        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
        copyright.Should().NotBeNull();
        copyright!.Copyright.Should().Contain("tecvia");
    }
}
