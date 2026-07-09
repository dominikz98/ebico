using EBICO.Server.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace EBICO.Server.Http;

/// <summary>
/// Maps the EBICS HTTP endpoint (POST, <c>text/xml</c>) that feeds the request pipeline.
/// </summary>
public static class EbicsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a <c>POST</c> EBICS endpoint at <paramref name="pattern"/>. The handler reads the body
    /// transport-safely and delegates to <see cref="IEbicsRequestPipeline"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern (e.g. <c>/ebics</c>).</param>
    /// <returns>A builder for further endpoint configuration.</returns>
    public static IEndpointConventionBuilder MapEbicsEndpoint(this IEndpointRouteBuilder endpoints, string pattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        return endpoints.MapPost(pattern, HandleAsync)
            .WithName("EbicsEndpoint");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IEbicsRequestPipeline pipeline,
        IOptions<EbicoServerOptions> options)
    {
        var ct = httpContext.RequestAborted;

        var read = await EbicsRequestReader.ReadAsync(httpContext.Request, options.Value, ct).ConfigureAwait(false);
        if (!read.Ok)
        {
            return Results.StatusCode(read.TransportStatusCode);
        }

        var result = await pipeline.ProcessAsync(read.Xml!, ct).ConfigureAwait(false);
        return Results.Bytes(result.Body, result.ContentType);
    }
}
