using EBICO.Core;
using EBICO.Core.Administrative;

namespace EBICO.Connector.Download;

/// <summary>
/// Convenience download request for the requesting subscriber's <b>customer and subscriber data</b>
/// (order type <c>HTD</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HTD"</c> (an administrative order type, kept as the
/// <c>AdminOrderType</c> on H005).
/// </summary>
public sealed class HtdDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period (honoured only where the version's order params carry a date range).</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatusProtocolOrderTypes.SubscriberData;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for the <b>customer data including all subscribers</b> (order type
/// <c>HKD</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HKD"</c>.
/// </summary>
public sealed class HkdDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period (honoured only where the version's order params carry a date range).</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatusProtocolOrderTypes.CustomerData;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for the <b>order types available for download</b> (order type
/// <c>HAA</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HAA"</c>.
/// </summary>
public sealed class HaaDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period (honoured only where the version's order params carry a date range).</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatusProtocolOrderTypes.AvailableOrderTypes;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for the <b>bank parameters</b> (access and protocol parameters, order
/// type <c>HPD</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HPD"</c>.
/// </summary>
public sealed class HpdDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period (honoured only where the version's order params carry a date range).</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatusProtocolOrderTypes.BankParameters;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for the <b>machine-readable customer protocol</b> (order type <c>HAC</c>,
/// an XML projection over the event log). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"HAC"</c>.
/// </summary>
public sealed class HacDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period (honoured only where the version's order params carry a date range).</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatusProtocolOrderTypes.CustomerProtocolXml;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for the <b>textual customer protocol</b> (order type <c>PTK</c>, a text
/// projection over the event log). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"PTK"</c>.
/// </summary>
public sealed class PtkDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period (honoured only where the version's order params carry a date range).</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatusProtocolOrderTypes.CustomerProtocolText;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}
