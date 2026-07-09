namespace EBICO.Connector;

/// <summary>
/// Marker for an application-facing EBICS request that binds itself to the result it
/// produces. The generic parameter lets <see cref="IEbicsClient.Send{TResult}"/> return a
/// strongly typed <see cref="EbicsResult{T}"/> without the caller specifying the result type.
/// </summary>
/// <remarks>
/// This is the connector's <em>application-layer</em> request abstraction. It is deliberately
/// separate from the protocol-level envelope interfaces in <c>EBICO.Core.Versioning</c>
/// (<c>IEbicsRequestEnvelope</c>/<c>IEbicsResponseEnvelope</c>), which live on a different
/// layer. Concrete requests (onboarding, upload, download) are added by later M6 issues; a
/// request carries only data, never logic.
/// </remarks>
/// <typeparam name="TResult">The type produced when the request succeeds.</typeparam>
public interface IEbicsRequest<TResult>
{
}
