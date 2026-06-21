namespace EBICO.Core.Versioning;

/// <summary>
/// Marker for an EBICS envelope sent from client to server (e.g. <c>ebicsRequest</c>,
/// <c>ebicsUnsecuredRequest</c>, <c>ebicsUnsignedRequest</c>,
/// <c>ebicsNoPubKeyDigestsRequest</c>).
/// </summary>
/// <remarks>
/// Named with the <c>Envelope</c> suffix to keep it distinct from the connector's
/// future app-facing <c>IEbicsRequest&lt;TResult&gt;</c> request abstraction
/// (see <c>docs/connector/architecture.md</c>); they live at different layers.
/// </remarks>
public interface IEbicsRequestEnvelope : IEbicsEnvelope
{
}
