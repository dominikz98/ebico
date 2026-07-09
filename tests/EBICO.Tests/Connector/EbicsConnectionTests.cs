using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Configuration;
using EBICO.Core;
using EBICO.Core.Versioning;

namespace EBICO.Tests.Connector;

/// <summary>Tests for <see cref="EbicsConnection.FromOptions"/> — resolution and error handling.</summary>
public class EbicsConnectionTests
{
    [Fact]
    public void FromOptions_MapsAllFields()
    {
        var connection = EbicsConnection.FromOptions(new EbicsConnectionOptions
        {
            Url = "https://bank.example/ebics",
            HostId = "HOST",
            PartnerId = "PART",
            UserId = "USER",
            Version = EbicsVersion.H004,
        });

        connection.Url.Should().Be(new Uri("https://bank.example/ebics"));
        connection.HostId.Value.Should().Be("HOST");
        connection.PartnerId.Value.Should().Be("PART");
        connection.UserId.Value.Should().Be("USER");
        connection.Version.Should().Be(EbicsVersion.H004);
        connection.VersionInfo.Code.Should().Be("H004");
        connection.VersionInfo.Should().BeSameAs(EbicsVersions.Get(EbicsVersion.H004));
    }

    [Fact]
    public void FromOptions_InvalidOptions_ThrowsConfigurationException()
    {
        var act = () => EbicsConnection.FromOptions(new EbicsConnectionOptions
        {
            Url = "bad",
            HostId = null,
            PartnerId = null,
            UserId = null,
        });

        act.Should().Throw<EbicsConfigurationException>();
    }

    [Fact]
    public void FromOptions_Null_Throws()
    {
        var act = () => EbicsConnection.FromOptions(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
