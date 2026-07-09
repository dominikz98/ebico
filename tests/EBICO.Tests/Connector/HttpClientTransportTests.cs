using System.Net;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Transport;
using EBICO.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Connector;

/// <summary>Tests for the default <c>HttpClientTransport</c> via a stubbed <see cref="HttpMessageHandler"/>.</summary>
public class HttpClientTransportTests
{
    private const string Url = "https://bank.example/ebics";

    private static ServiceProvider BuildProvider(StubHttpMessageHandler stub)
    {
        var services = new ServiceCollection();
        services.AddEbicoConnector(o =>
        {
            o.Url = Url;
            o.HostId = "HOST";
            o.PartnerId = "PART";
            o.UserId = "USER";
            o.Version = EbicsVersion.H005;
        }).ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SendAsync_PostsPayloadToConfiguredUrl_AndReturnsResponse()
    {
        var responseBytes = "<response/>"u8.ToArray();
        var stub = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(responseBytes) });
        using var provider = BuildProvider(stub);
        var transport = provider.GetRequiredService<ITransport>();

        var requestBytes = "<request/>"u8.ToArray();
        var response = await transport.SendAsync(
            new EbicsHttpRequest { Payload = requestBytes }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(200);
        response.Payload.ToArray().Should().Equal(responseBytes);
        stub.LastRequest!.Method.Should().Be(HttpMethod.Post);
        stub.LastRequest.RequestUri.Should().Be(new Uri(Url));
        stub.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("text/xml");
        stub.LastRequestBody.Should().Equal(requestBytes);
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatus_ThrowsTransportException()
    {
        var stub = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var provider = BuildProvider(stub);
        var transport = provider.GetRequiredService<ITransport>();

        var act = async () => await transport.SendAsync(
            new EbicsHttpRequest { Payload = "x"u8.ToArray() }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<EbicsTransportException>();
    }

    [Fact]
    public async Task SendAsync_HttpRequestFailure_ThrowsTransportException()
    {
        var stub = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        using var provider = BuildProvider(stub);
        var transport = provider.GetRequiredService<ITransport>();

        var act = async () => await transport.SendAsync(
            new EbicsHttpRequest { Payload = "x"u8.ToArray() }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<EbicsTransportException>();
    }

    [Fact]
    public async Task SendAsync_CallerCancellation_Propagates()
    {
        var stub = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = BuildProvider(stub);
        var transport = provider.GetRequiredService<ITransport>();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = async () => await transport.SendAsync(new EbicsHttpRequest { Payload = "x"u8.ToArray() }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
