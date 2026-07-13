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
        provider.GetRequiredService<IServerBankKeyStore>().Should().BeOfType<InMemoryServerBankKeyStore>();
        provider.GetRequiredService<IMasterDataManager>().Should().BeOfType<MasterDataManager>();
        provider.GetRequiredService<IEbicsRequestVerifier>().Should().BeOfType<NoOpEbicsRequestVerifier>();
        provider.GetRequiredService<IEbicsErrorMapper>().Should().BeOfType<EbicsErrorMapper>();
        provider.GetRequiredService<IEbicsOrderHandlerResolver>().Should().BeOfType<EbicsOrderHandlerResolver>();
        provider.GetRequiredService<EbicsResponseFactory>().Should().NotBeNull();
    }

    [Fact]
    public void AddEbicoServer_RegistersKeyManagementOrderHandlersPerVersion()
    {
        var services = new ServiceCollection();
        services.AddEbicoServer();
        using var provider = services.BuildServiceProvider();

        var handlers = provider.GetServices<IEbicsOrderHandler>().ToArray();

        // The key-management order types the server handles.
        var expectedOrderTypes = new[] { "INI", "HIA", "HPB", "HCA", "HCS", "SPR", "HSA" };
        handlers.Should().OnlyContain(h => expectedOrderTypes.Contains(h.OrderType));

        // INI/HIA/HPB and the key-change/suspension orders HCA/HCS/SPR exist for every protocol version.
        foreach (var orderType in new[] { "INI", "HIA", "HPB", "HCA", "HCS", "SPR" })
        {
            handlers.Where(h => h.OrderType == orderType).Select(h => h.Version).Should().BeEquivalentTo(
                [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005]);
        }

        // HSA was removed in H005, so it exists only for H003/H004.
        handlers.Where(h => h.OrderType == "HSA").Select(h => h.Version).Should().BeEquivalentTo(
            [EbicsVersion.H003, EbicsVersion.H004]);
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
