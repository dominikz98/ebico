using EBICO.Core;

namespace EBICO.Connector.Configuration;

/// <summary>
/// The connection parameters for a single EBICS subscriber, supplied via
/// <c>AddEbicoConnector</c>. Kept as a mutable, binding-friendly options object (settable
/// strings, parameterless constructor) so it works with the standard options pattern and
/// configuration binding. It is validated and turned into an immutable
/// <see cref="EbicsConnection"/> before use.
/// </summary>
public sealed class EbicsConnectionOptions
{
    /// <summary>The absolute HTTP(S) URL of the EBICS server endpoint.</summary>
    public string? Url { get; set; }

    /// <summary>The EBICS host identifier (<c>HostID</c>) of the target bank/server.</summary>
    public string? HostId { get; set; }

    /// <summary>The EBICS partner identifier (<c>PartnerID</c>) — the customer.</summary>
    public string? PartnerId { get; set; }

    /// <summary>The EBICS user identifier (<c>UserID</c>) — the subscriber.</summary>
    public string? UserId { get; set; }

    /// <summary>The target EBICS protocol version. Defaults to <see cref="EbicsVersion.H005"/>.</summary>
    public EbicsVersion Version { get; set; } = EbicsVersion.H005;
}
