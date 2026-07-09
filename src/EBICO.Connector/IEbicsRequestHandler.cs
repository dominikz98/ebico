namespace EBICO.Connector;

/// <summary>
/// Handles one concrete <see cref="IEbicsRequest{TResult}"/> type. Handlers hold the actual
/// EBICS logic for a request (build the envelope, run the transaction skeleton, interpret the
/// response) and are resolved by <see cref="IEbicsClient"/> per request type.
/// </summary>
/// <remarks>
/// Concrete handlers (onboarding INI/HIA/HPB, upload, download) are added by later M6 issues.
/// #46 provides only the dispatch that resolves and invokes them.
/// </remarks>
/// <typeparam name="TRequest">The concrete request type.</typeparam>
/// <typeparam name="TResult">The result type the request produces.</typeparam>
public interface IEbicsRequestHandler<TRequest, TResult>
    where TRequest : IEbicsRequest<TResult>
{
    /// <summary>Handles the request within the given execution context.</summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="ctx">The per-send execution context (connection, keys, transport).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The business result of handling the request.</returns>
    Task<EbicsResult<TResult>> Handle(TRequest request, EbicsContext ctx, CancellationToken ct);
}
