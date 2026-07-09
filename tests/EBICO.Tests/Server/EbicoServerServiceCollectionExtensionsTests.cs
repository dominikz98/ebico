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
        provider.GetRequiredService<IEbicsRequestVerifier>().Should().BeOfType<NoOpEbicsRequestVerifier>();
        provider.GetRequiredService<IEbicsErrorMapper>().Should().BeOfType<EbicsErrorMapper>();
        provider.GetRequiredService<IEbicsOrderHandlerResolver>().Should().BeOfType<EbicsOrderHandlerResolver>();
        provider.GetRequiredService<EbicsResponseFactory>().Should().NotBeNull();
    }

    [Fact]
    public void AddEbicoServer_RegistersNoOrderHandlers()
    {
        var services = new ServiceCollection();
        services.AddEbicoServer();
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IEbicsOrderHandler>().Should().BeEmpty();
    }

    [Fact]
    public void AddEbicoServer_AppliesDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddEbicoServer();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EbicoServerOptions>>().Value;

        options.EndpointPath.Should().Be("/ebics");
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
