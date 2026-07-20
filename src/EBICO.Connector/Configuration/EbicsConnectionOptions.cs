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

    /// <summary>
    /// An optional client-side allow-list of the (classical) order-type codes the subscriber may submit
    /// (e.g. <c>"CCT"</c>, <c>"C53"</c>). When non-empty, the connector rejects a request whose effective
    /// order type is not listed <em>before</em> contacting the server (a fast-fail that saves a round-trip),
    /// mirroring the return code the bank would report. An <b>empty</b> list (the default) disables the
    /// client-side check and defers authorisation entirely to the server. This is a convenience guard, not
    /// the authorisation authority — the bank remains authoritative. The property is get-only so it binds
    /// via the standard options/configuration pattern; add entries with <c>o.AllowedOrderTypes.Add("CCT")</c>.
    /// </summary>
    public IList<string> AllowedOrderTypes { get; } = [];
}
