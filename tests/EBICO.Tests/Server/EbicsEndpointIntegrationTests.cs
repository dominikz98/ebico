extern alias EbicoServer;
using System.Net;
using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end tests for the EBICS HTTP endpoint of the server host skeleton (issue #25), driven
/// through <see cref="WebApplicationFactory{TEntryPoint}"/>: a well-formed request yields HTTP 200
/// with a well-formed EBICS error response; transport problems yield 4xx.
/// </summary>
public class EbicsEndpointIntegrationTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public EbicsEndpointIntegrationTests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    [Fact]
    public async Task PostEbics_WellFormedRequestUnknownOrderType_Returns200_WithUnsupportedOrderType()
    {
        var client = _factory.CreateClient();
        var content = new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091006");
    }

    [Fact]
    public async Task PostEbics_MalformedXml_Returns200_WithInvalidXml()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("<not-xml", Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091010");
    }

    [Fact]
    public async Task PostEbics_EmptyBody_Returns200_WithInvalidXml()
    {
        var client = _factory.CreateClient();
        var content = new StringContent(string.Empty, Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091010");
    }

    [Fact]
    public async Task PostEbics_UnsupportedVersionNamespace_Returns200_WithErrorInFallbackVersion()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("<ebicsRequest xmlns=\"urn:example:unknown\"/>", Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        envelope.ProtocolVersion.Should().Be(EbicsVersion.H005);
        ServerTestHelpers.ReadReturnCodes(envelope).HeaderCode.Should().Be("061002");
    }

    [Fact]
    public async Task PostEbics_WrongContentType_Returns415()
    {
        var client = _factory.CreateClient();
        var content = new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task PostEbics_BodyTooLarge_Returns413()
    {
        var client = _factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                s.Configure<EbicoServerOptions>(o => o.MaxRequestBodyBytes = 32)))
            .CreateClient();
        var content = new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }
}
