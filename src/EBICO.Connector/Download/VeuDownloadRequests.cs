using EBICO.Core;
using EBICO.Core.Administrative;

namespace EBICO.Connector.Download;

/// <summary>
/// Convenience download request for the <b>overview of orders awaiting distributed signatures</b>
/// (order type <c>HVU</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HVU"</c> (an administrative order type, kept as the
/// <c>AdminOrderType</c> on H005).
/// </summary>
/// <remarks>
/// The response lists the parked orders with the <c>OrderID</c> that <see cref="HvdDownloadRequest"/>,
/// <see cref="HvtDownloadRequest"/> and the upload requests <c>HveUploadRequest</c>/<c>HvsUploadRequest</c>
/// reference. See <c>docs/connector/veu.md</c>.
/// </remarks>
public sealed class HvuDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => VeuOrderTypes.Overview;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => null;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for the <b>overview of awaiting orders including payment details</b>
/// (order type <c>HVZ</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HVZ"</c>.
/// </summary>
public sealed class HvzDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => VeuOrderTypes.OverviewWithDetails;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => null;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for the <b>status of one awaiting order</b> (order type <c>HVD</c>).
/// Equivalent to a <see cref="DownloadRequest"/> with <see cref="DownloadRequest.OrderType"/> =
/// <c>"HVD"</c> and <see cref="DownloadRequest.Veu"/> set.
/// </summary>
public sealed class HvdDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>The parked order to ask about; its <see cref="VeuOrderReference.OrderId"/> comes from <c>HVU</c>/<c>HVZ</c>. Required.</summary>
    public required VeuOrderReference Order { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => VeuOrderTypes.Detail;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => null;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;

    /// <inheritdoc />
    VeuOrderReference? IDownloadConvenienceRequest.Veu => Order;
}

/// <summary>
/// Convenience download request for the <b>transaction details of one awaiting order</b> (order type
/// <c>HVT</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HVT"</c> and <see cref="DownloadRequest.Veu"/> set.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec caveat:</b> the EBICO emulator answers HVT order-summarily and does not decompose the
/// underlying ISO 20022 message into single transactions (see
/// <c>docs/server/order-coverage-matrix.md</c>).
/// </remarks>
public sealed class HvtDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>The parked order to ask about; its <see cref="VeuOrderReference.OrderId"/> comes from <c>HVU</c>/<c>HVZ</c>. Required.</summary>
    public required VeuOrderReference Order { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => VeuOrderTypes.TransactionDetail;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => null;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;

    /// <inheritdoc />
    VeuOrderReference? IDownloadConvenienceRequest.Veu => Order;
}
