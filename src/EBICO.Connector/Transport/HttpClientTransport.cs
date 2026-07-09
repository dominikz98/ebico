using System.Net.Http.Headers;
using EBICO.Connector.Configuration;

namespace EBICO.Connector.Transport;

/// <summary>
/// The default <see cref="ITransport"/>: POSTs the serialized EBICS envelope to the configured
/// server URL over an <c>HttpClient</c> obtained from <c>IHttpClientFactory</c> (the named client
/// <see cref="EbicoConnector.HttpClientName"/>). Timeouts and resilience are configured by the
/// caller on that named client and therefore stay out of the connector core.
/// </summary>
internal sealed class HttpClientTransport : ITransport
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EbicsConnection _connection;

    public HttpClientTransport(IHttpClientFactory httpClientFactory, EbicsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(connection);

        _httpClientFactory = httpClientFactory;
        _connection = connection;
    }

    /// <inheritdoc />
    public async Task<EbicsHttpResponse> SendAsync(EbicsHttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = _httpClientFactory.CreateClient(EbicoConnector.HttpClientName);

        using var content = new ByteArrayContent(request.Payload.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };

        try
        {
            using var response = await client.PostAsync(_connection.Url, content, ct).ConfigureAwait(false);
            var payload = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new EbicsTransportException(
                    $"The EBICS server returned HTTP status {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            return new EbicsHttpResponse
            {
                StatusCode = (int)response.StatusCode,
                Payload = payload,
            };
        }
        catch (HttpRequestException ex)
        {
            throw new EbicsTransportException("The EBICS transport failed to complete the HTTP request.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // A timeout surfaces as TaskCanceledException; caller cancellation is left to propagate.
            throw new EbicsTransportException("The EBICS transport request timed out.", ex);
        }
    }
}
