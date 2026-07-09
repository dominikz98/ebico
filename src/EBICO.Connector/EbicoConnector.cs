namespace EBICO.Connector;

/// <summary>
/// Well-known constants for the EBICO connector, shared across the transport and the DI wiring.
/// </summary>
public static class EbicoConnector
{
    /// <summary>
    /// The logical name of the <c>HttpClient</c> the connector uses (registered via
    /// <c>AddHttpClient</c>). Callers may configure timeouts and resilience on this named client.
    /// </summary>
    public const string HttpClientName = "EBICO.Connector";
}
