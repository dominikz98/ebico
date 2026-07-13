using EBICO.Core;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using H = EBICO.Core.Schema.H003;

namespace EBICO.Server.Handlers;

/// <summary>The H003 (EBICS 2.4) SPR handler. Reads the subscriber identifiers from the signed <c>ebicsRequest</c>.</summary>
public sealed class H003SprOrderHandler : SprOrderHandlerBase
{
    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager.</param>
    public H003SprOrderHandler(IMasterDataManager masterData)
        : base(masterData)
    {
    }

    /// <inheritdoc />
    public override EbicsVersion Version => EbicsVersion.H003;

    /// <inheritdoc />
    protected override SprRequestData ExtractHeader(EbicsRequestContext context)
    {
        var request = context.Envelope as H.EbicsRequest
            ?? throw new InvalidDataException("The SPR request is not an H003 ebicsRequest.");
        var header = request.Header?.Static;
        return new SprRequestData(header?.HostId, header?.PartnerId, header?.UserId);
    }
}
