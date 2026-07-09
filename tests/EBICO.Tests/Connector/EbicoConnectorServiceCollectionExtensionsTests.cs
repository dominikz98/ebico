using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Configuration;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EBICO.Tests.Connector;

/// <summary>Tests for <c>AddEbicoConnector</c> service registration and the returned builder.</summary>
public class EbicoConnectorServiceCollectionExtensionsTests
{
    private const string Url = "https://bank.example/ebics";

    private static void Configure(EbicsConnectionOptions o)
    {
        o.Url = Url;
        o.HostId = "HOST";
        o.PartnerId = "PART";
        o.UserId = "USER";
        o.Version = EbicsVersion.H005;
    }

    [Fact]
    public void AddEbicoConnector_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddEbicoConnector(Configure);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEbicsClient>().Should().NotBeNull();
        provider.GetRequiredService<IKeyStore>().Should().BeOfType<InMemoryKeyStore>();
        provider.GetRequiredService<ITransport>().Should().NotBeNull();
        provider.GetRequiredService<IHttpClientFactory>().Should().NotBeNull();
        provider.GetRequiredService<EbicsConnection>().Url.Should().Be(new Uri(Url));
    }

    [Fact]
    public void AddEbicoConnector_ReturnsHttpClientBuilder_ForTheNamedClient()
    {
        var services = new ServiceCollection();

        var builder = services.AddEbicoConnector(Configure);

        builder.Should().BeAssignableTo<IHttpClientBuilder>();
        builder.Name.Should().Be(EbicoConnector.HttpClientName);
    }

    [Fact]
    public void AddEbicoConnector_ConfiguresNamedClientBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddEbicoConnector(Configure);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(EbicoConnector.HttpClientName);

        client.BaseAddress.Should().Be(new Uri(Url));
    }

    [Fact]
    public void AddEbicoConnector_InvalidOptions_ThrowOnConnectionResolution()
    {
        var services = new ServiceCollection();
        services.AddEbicoConnector(o => o.Url = "bad");
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<EbicsConnection>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddEbicoConnector_NullConfigure_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddEbicoConnector(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
