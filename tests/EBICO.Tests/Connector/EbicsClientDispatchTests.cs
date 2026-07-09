using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Keys;
using EBICO.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Connector;

/// <summary>Tests for the connector's own request dispatch in <c>EbicsClient</c> (no MediatR, ADR-0005).</summary>
public class EbicsClientDispatchTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddEbicoConnector(o =>
        {
            o.Url = "https://bank.example/ebics";
            o.HostId = "HOST";
            o.PartnerId = "PART";
            o.UserId = "USER";
            o.Version = EbicsVersion.H005;
        });
        return services;
    }

    [Fact]
    public async Task Send_InvokesRegisteredHandler_AndReturnsResult()
    {
        var services = BaseServices();
        var handler = new FakeHandler();
        services.AddSingleton<IEbicsRequestHandler<FakeRequest, FakeResult>>(handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEbicsClient>();

        var result = await client.Send(new FakeRequest { Input = "abc" }, TestContext.Current.CancellationToken);

        handler.WasCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.Value!.Payload.Should().Be("handled:abc");
    }

    [Fact]
    public async Task Send_BuildsContextWithResolvedCollaborators()
    {
        var services = BaseServices();
        var handler = new FakeHandler();
        services.AddSingleton<IEbicsRequestHandler<FakeRequest, FakeResult>>(handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEbicsClient>();

        await client.Send(new FakeRequest(), TestContext.Current.CancellationToken);

        handler.LastContext.Should().NotBeNull();
        handler.LastContext!.Connection.HostId.Value.Should().Be("HOST");
        handler.LastContext.Version.Code.Should().Be("H005");
        handler.LastContext.Keys.Should().BeOfType<InMemoryKeyStore>();
        handler.LastContext.Transport.Should().NotBeNull();
    }

    [Fact]
    public async Task Send_NoHandlerRegistered_ThrowsConfigurationException()
    {
        using var provider = BaseServices().BuildServiceProvider();
        var client = provider.GetRequiredService<IEbicsClient>();

        var act = async () => await client.Send(new FakeRequest(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
    }

    [Fact]
    public async Task Send_NullRequest_Throws()
    {
        using var provider = BaseServices().BuildServiceProvider();
        var client = provider.GetRequiredService<IEbicsClient>();

        var act = async () => await client.Send<FakeResult>(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
