using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using H004 = EBICO.Core.Schema.H004;

namespace EBICO.Tests.Server;

/// <summary>
/// Shared helpers for the EBICO.Server tests (issue #25): builds well-formed request XML from the
/// committed Core bindings (no proprietary fixtures) and reads the return codes out of a response.
/// </summary>
internal static class ServerTestHelpers
{
    /// <summary>
    /// Builds a well-formed H004 <c>ebicsRequest</c> carrying <paramref name="orderType"/> in its
    /// static header, serialized to a string.
    /// </summary>
    /// <param name="orderType">The three-character order type (e.g. <c>"AAA"</c>).</param>
    /// <returns>The serialized request XML.</returns>
    public static string BuildH004Request(string orderType)
    {
        var request = new H004.EbicsRequest
        {
            Version = "H004",
            Header = new H004.EbicsRequestHeader
            {
                Static = new H004.StaticHeaderType
                {
                    HostId = "EBICOHOST",
                    PartnerId = "PARTNER01",
                    UserId = "USER01",
                    OrderDetails = new H004.StaticHeaderOrderDetailsType
                    {
                        OrderType = new H004.StaticHeaderOrderDetailsTypeOrderType { Value = orderType },
                    },
                },
                Mutable = new H004.MutableHeaderType(),
            },
        };

        return EbicsXmlSerializer.SerializeToString(request, EbicsVersion.H004);
    }

    /// <summary>Reads the header (technical) and body (business) return codes of a response envelope.</summary>
    /// <param name="envelope">The response envelope.</param>
    /// <returns>The mutable-header return code and the body return code.</returns>
    public static (string? HeaderCode, string? BodyCode) ReadReturnCodes(IEbicsEnvelope envelope) => envelope switch
    {
        EBICO.Core.Schema.H003.EbicsResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        H004.EbicsResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        EBICO.Core.Schema.H005.EbicsResponse r => (r.Header?.Mutable?.ReturnCode, r.Body?.ReturnCode?.Value),
        _ => (null, null),
    };
}
