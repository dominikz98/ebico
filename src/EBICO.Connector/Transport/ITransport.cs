namespace EBICO.Connector.Transport;

/// <summary>
/// The connector's narrow transport abstraction. It exchanges a serialized EBICS XML envelope
/// with the server and returns the raw response, keeping the client core transport-agnostic and
/// testable. The default implementation wraps an <c>HttpClient</c> obtained from
/// <c>IHttpClientFactory</c>; alternative implementations (in-memory, recording) are trivial to
/// substitute in tests.
/// </summary>
public interface ITransport
{
    /// <summary>Sends a serialized EBICS request and returns the server response.</summary>
    /// <param name="request">The request carrying the serialized XML payload.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The server response with status code and payload.</returns>
    /// <exception cref="EbicsTransportException">The exchange failed at the transport level.</exception>
    Task<EbicsHttpResponse> SendAsync(EbicsHttpRequest request, CancellationToken ct = default);
}
