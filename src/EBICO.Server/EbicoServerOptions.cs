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
    /// The route prefix the master-data admin API is mounted at. Defaults to <c>/admin</c>. The
    /// admin API is unauthenticated and intended for local/emulator use only.
    /// </summary>
    public string AdminApiPath { get; set; } = "/admin";

    /// <summary>
    /// The version used to produce an error response when the request version cannot be detected
    /// (e.g. malformed XML). Defaults to <see cref="EbicsVersion.H005"/>.
    /// </summary>
    public EbicsVersion FallbackResponseVersion { get; set; } = EbicsVersion.H005;

    /// <summary>The maximum accepted request body size in bytes. Defaults to 1 MiB.</summary>
    public long MaxRequestBodyBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// The maximum raw (pre-base64) size in bytes of a single order-data segment. Defaults to 512 KiB.
    /// The base64 wire size is roughly 4/3 of this (512 KiB &#8594; ~683 KiB), leaving headroom for the
    /// envelope under <see cref="MaxRequestBodyBytes"/> (1 MiB); the hard ceiling that keeps a segment's
    /// base64 form at or below 1 MiB is 768 KiB raw. Consumed by <c>EbicsSegmentation.Split</c> once the
    /// transaction engine (M4 upload/download, issues #32/#33) wires it in.
    /// </summary>
    public int SegmentSizeBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// The content types accepted on the EBICS endpoint. Defaults to <c>text/xml</c> and
    /// <c>application/xml</c>.
    /// </summary>
    public IReadOnlyCollection<string> AllowedContentTypes { get; set; } = ["text/xml", "application/xml"];
}
