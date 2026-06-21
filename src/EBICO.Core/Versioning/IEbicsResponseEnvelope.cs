namespace EBICO.Core.Versioning;

/// <summary>
/// Marker for an EBICS envelope sent from server to client (e.g. <c>ebicsResponse</c>,
/// <c>ebicsKeyManagementResponse</c>).
/// </summary>
public interface IEbicsResponseEnvelope : IEbicsEnvelope
{
}
