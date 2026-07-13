using EBICO.Core;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H004;

namespace EBICO.Server.Handlers;

/// <summary>The H004 (EBICS 2.5) SPR handler. Reads the subscriber identifiers from the signed <c>ebicsRequest</c>.</summary>
public sealed class H004SprOrderHandler : SprOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    public H004SprOrderHandler(IMasterDataManager masterData)
        : base(masterData)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H004;

    /// <inheritdoc />
    protected override SprRequestData ExtractHeader(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsRequest
            ?? throw new InvalidDataException("The SPR request is not an H004 ebicsRequest.");
        var header = request.Header?.Static;
        return new SprRequestData(header?.HostId, header?.PartnerId, header?.UserId);
    }
}
