extern alias EbicoServer;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EBICO.Tests.Server;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end tests for the container-relevant host wiring (issue #61), driven through
/// <see cref="WebApplicationFactory{TEntryPoint}"/>: the <c>/health</c> liveness endpoint answers 200,
/// and the <c>Ebico</c> configuration section (the way the container overrides options via
/// <c>Ebico__EndpointPath</c>) actually drives the mapped EBICS endpoint path.
/// </summary>
public class HealthEndpointIntegrationTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public HealthEndpointIntegrationTests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    [Fact]
    public async Task GetHealth_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EbicoConfiguredEndpointPath_ControlsTheMappedEbicsPath()
    {
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ebico:EndpointPath"] = "/custom-ebics",
            })));
        var client = factory.CreateClient();

        // The endpoint is now mapped at the configured path...
        var custom = await client.PostAsync(
            "/custom-ebics", new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "text/xml"), _ct);
        custom.StatusCode.Should().Be(HttpStatusCode.OK);

        // ...and no longer at the default path.
        var defaultPath = await client.PostAsync(
            "/ebics", new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "text/xml"), _ct);
        defaultPath.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
