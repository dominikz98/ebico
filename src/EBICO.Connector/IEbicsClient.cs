namespace EBICO.Connector;

/// <summary>
/// The connector's single public entry point (the mediator). The calling application hands a
/// request object to <see cref="Send{TResult}"/> and receives a typed
/// <see cref="EbicsResult{T}"/>; all EBICS complexity (validation, serialization, crypto,
/// transport, segmentation) lives behind this method.
/// </summary>
/// <remarks>
/// The client resolves the matching <see cref="IEbicsRequestHandler{TRequest, TResult}"/> for
/// each request at runtime via its own dispatch (no MediatR — see ADR-0005). Technical failures
/// (network, HTTP, signature, malformed XML) surface as exceptions; business return codes are
/// carried in <see cref="EbicsResult{T}"/>.
/// </remarks>
public interface IEbicsClient
{
    /// <summary>Sends an EBICS request and returns its typed result.</summary>
    /// <typeparam name="TResult">The result type the request produces.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The business result, including EBICS return code and text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="EbicsConfigurationException">No handler is registered for the request type.</exception>
    /// <exception cref="EbicsTransportException">A transport-level failure occurred.</exception>
    Task<EbicsResult<TResult>> Send<TResult>(IEbicsRequest<TResult> request, CancellationToken ct = default);
}
