namespace EBICO.Connector.Transport;

/// <summary>A transport request carrying the serialized EBICS XML envelope to POST to the server.</summary>
public sealed class EbicsHttpRequest
{
    /// <summary>The serialized EBICS XML envelope (UTF-8) to send as the request body.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }
}

/// <summary>A transport response carrying the server's HTTP status and raw response body.</summary>
public sealed class EbicsHttpResponse
{
    /// <summary>The HTTP status code returned by the server.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The raw response body bytes (an EBICS XML envelope).</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }
}
