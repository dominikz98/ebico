using EBICO.Core.Administrative;
using EBICO.Server.State;

namespace EBICO.Server.Orders;

/// <summary>
/// The <see cref="IDownloadOrderProcessor"/> for the distributed-electronic-signature download orders
/// (issue #42): HVU/HVZ (overview of the orders awaiting signatures) and HVD/HVT (status/detail of a single
/// order). It projects the partner's open orders from the <see cref="IOpenVeuStore"/> and renders the
/// per-version response bindings via <see cref="VeuResponseBuilder"/>.
/// </summary>
/// <remarks>
/// HVU/HVZ always produce a document — an empty overview when the partner has no orders awaiting signatures.
/// HVD/HVT address a single order via the request's order id and <em>decline</em> (return
/// <see langword="null"/>, which the download engine reports as <c>EBICS_NO_DOWNLOAD_DATA_AVAILABLE</c>) when
/// the id is missing or does not identify an open order. See <c>docs/server/veu-orders.md</c> and ADR-0020.
/// </remarks>
public sealed class VeuOverviewDownloadProcessor : IDownloadOrderProcessor
{
    private readonly IOpenVeuStore _veuStore;

    /// <summary>Initializes the processor.</summary>
    /// <param name="veuStore">The open-VEU store the overview/detail is projected from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="veuStore"/> is <see langword="null"/>.</exception>
    public VeuOverviewDownloadProcessor(IOpenVeuStore veuStore)
    {
        ArgumentNullException.ThrowIfNull(veuStore);
        _veuStore = veuStore;
    }

    /// <inheritdoc />
    public bool CanProcess(string? effectiveOrderType) => VeuOrderTypes.IsVeuDownloadOrderType(effectiveOrderType);

    /// <inheritdoc />
    public async Task<byte[]?> GenerateAsync(DownloadOrderRequest request, CancellationToken ct = default)
    {
        var host = request.Subscriber.HostId;
        var partner = request.Subscriber.PartnerId;

        switch (request.EffectiveOrderType)
        {
            case VeuOrderTypes.Overview:
            {
                var orders = await _veuStore.ListAsync(host, partner, ct).ConfigureAwait(false);
                return VeuResponseBuilder.BuildHvu(request.Version, ToViews(orders));
            }

            case VeuOrderTypes.OverviewWithDetails:
            {
                var orders = await _veuStore.ListAsync(host, partner, ct).ConfigureAwait(false);
                return VeuResponseBuilder.BuildHvz(request.Version, ToViews(orders));
            }

            case VeuOrderTypes.Detail:
            case VeuOrderTypes.TransactionDetail:
            {
                if (string.IsNullOrEmpty(request.OrderId))
                {
                    return null;
                }

                var order = await _veuStore.TryGetAsync(host, partner, request.OrderId, ct).ConfigureAwait(false);
                if (order is null)
                {
                    return null;
                }

                var view = ToView(order);
                return request.EffectiveOrderType == VeuOrderTypes.Detail
                    ? VeuResponseBuilder.BuildHvd(request.Version, view)
                    : VeuResponseBuilder.BuildHvt(request.Version, view);
            }

            default:
                return null;
        }
    }

    private static IReadOnlyList<VeuOrderView> ToViews(IReadOnlyList<OpenVeuOrder> orders)
        => orders.Select(ToView).ToArray();

    private static VeuOrderView ToView(OpenVeuOrder order) => new(
        order.OrderId,
        order.EffectiveOrderType,
        order.OrderData.Length,
        order.NumSigRequired,
        order.NumSigDone,
        order.ReadyToBeSigned,
        order.Originator,
        order.Signers,
        order.ComputeDataDigest());
}
