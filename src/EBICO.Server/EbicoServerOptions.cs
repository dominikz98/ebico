using EBICO.Core;

namespace EBICO.Server;

/// <summary>
/// Configuration for the hostable EBICS server (emulator).
/// </summary>
public sealed class EbicoServerOptions
{
    /// <summary>The HTTP path the EBICS endpoint is mapped to. Defaults to <c>/ebics</c>.</summary>
    public string EndpointPath { get; set; } = "/ebics";

    /// <summary>
    /// The version used to produce an error response when the request version cannot be detected
    /// (e.g. malformed XML). Defaults to <see cref="EbicsVersion.H005"/>.
    /// </summary>
    public EbicsVersion FallbackResponseVersion { get; set; } = EbicsVersion.H005;

    /// <summary>The maximum accepted request body size in bytes. Defaults to 1 MiB.</summary>
    public long MaxRequestBodyBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// The content types accepted on the EBICS endpoint. Defaults to <c>text/xml</c> and
    /// <c>application/xml</c>.
    /// </summary>
    public IReadOnlyCollection<string> AllowedContentTypes { get; set; } = ["text/xml", "application/xml"];
}
