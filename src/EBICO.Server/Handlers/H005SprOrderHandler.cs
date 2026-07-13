using EBICO.Core;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H005;

namespace EBICO.Server.Handlers;

/// <summary>The H005 (EBICS 3.0) SPR handler. Reads the subscriber identifiers from the signed <c>ebicsRequest</c>.</summary>
public sealed class H005SprOrderHandler : SprOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    public H005SprOrderHandler(IMasterDataManager masterData)
        : base(masterData)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H005;

    /// <inheritdoc />
    protected override SprRequestData ExtractHeader(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsRequest
            ?? throw new InvalidDataException("The SPR request is not an H005 ebicsRequest.");
        var header = request.Header?.Static;
        return new SprRequestData(header?.HostId, header?.PartnerId, header?.UserId);
    }
}
