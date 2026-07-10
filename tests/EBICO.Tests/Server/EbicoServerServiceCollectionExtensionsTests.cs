using AwesomeAssertions;
using EBICO.Core;
using EBICO.Server;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <c>AddEbicoServer</c> — the DI wiring of the server host skeleton (issue #25):
/// resolvable services, skeleton defaults and option configuration.
/// </summary>
public class EbicoServerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEbicoServer_ResolvesPipelineAndSkeletonDefaults()
    {
        var services = new ServiceCollection();
        services.AddEbicoServer();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEbicsRequestPipeline>().Should().BeOfType<EbicsRequestPipeline>();
        provider.GetRequiredService<IEbicsStateStore>().Should().BeOfType<InMemoryEbicsStateStore>();
        provider.GetRequiredService<IServerKeyStore>().Should().BeOfType<InMemoryServerKeyStore>();
        provider.GetRequiredService<IMasterDataManager>().Should().BeOfType<MasterDataManager>();
        provider.GetRequiredService<IEbicsRequestVerifier>().Should().BeOfType<NoOpEbicsRequestVerifier>();
        provider.GetRequiredService<IEbicsErrorMapper>().Should().BeOfType<EbicsErrorMapper>();
        provider.GetRequiredService<IEbicsOrderHandlerResolver>().Should().BeOfType<EbicsOrderHandlerResolver>();
        provider.GetRequiredService<EbicsResponseFactory>().Should().NotBeNull();
    }

    [Fact]
    public void AddEbicoServer_RegistersIniOrderHandlerPerVersion()
    {
        var services = new ServiceCollection();
        services.AddEbicoServer();
        using var provider = services.BuildServiceProvider();

        var handlers = provider.GetServices<IEbicsOrderHandler>().ToArray();

        // One INI handler per protocol version, all serving order type "INI".
        handlers.Should().OnlyContain(h => h.OrderType == "INI");
        handlers.Select(h => h.Version).Should().BeEquivalentTo(
            [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005]);
    }

    [Fact]
    public void AddEbicoServer_AppliesDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddEbicoServer();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

        options.EndpointPath.Should().Be("/ebics");
        options.AdminApiPath.Should().Be("/admin");
        options.FallbackResponseVersion.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void AddEbicoServer_ConfigureOverridesOptions()
    {
        var services = new ServiceCollection();
        services.AddEbicoServer(o =>
        {
            o.EndpointPath = "/ebicsweb";
            o.FallbackResponseVersion = EbicsVersion.H004;
        });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

        options.EndpointPath.Should().Be("/ebicsweb");
        options.FallbackResponseVersion.Should().Be(EbicsVersion.H004);
    }

    [Fact]
    public void AddEbicoServer_NullServices_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddEbicoServer();

        act.Should().Throw<ArgumentNullException>();
    }
}
