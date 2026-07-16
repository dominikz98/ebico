using EBICO.Core;
using EBICO.Core.Statements;

namespace EBICO.Connector.Download;

/// <summary>
/// Convenience download request for an <b>account statement, SWIFT MT940</b> (order type <c>STA</c>).
/// Equivalent to a <see cref="DownloadRequest"/> with <see cref="DownloadRequest.OrderType"/> = <c>"STA"</c>.
/// </summary>
public sealed class StaDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period for the statement.</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data (typically a ZIP container) before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatementOrderTypes.StatementMt940;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for an <b>interim transaction report, SWIFT MT942</b> (order type
/// <c>VMK</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"VMK"</c>.
/// </summary>
public sealed class VmkDownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period for the report.</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data (typically a ZIP container) before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatementOrderTypes.InterimReportMt942;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for a <b>bank-to-customer statement, camt.053</b> (order type <c>C53</c>).
/// Equivalent to a <see cref="DownloadRequest"/> with <see cref="DownloadRequest.OrderType"/> = <c>"C53"</c>.
/// </summary>
public sealed class C53DownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period for the statement.</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data (typically a ZIP container) before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatementOrderTypes.StatementCamt053;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for a <b>bank-to-customer account report, camt.052</b> (order type
/// <c>C52</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"C52"</c>.
/// </summary>
public sealed class C52DownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period for the report.</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data (typically a ZIP container) before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatementOrderTypes.ReportCamt052;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}

/// <summary>
/// Convenience download request for a <b>bank-to-customer debit/credit notification, camt.054</b> (order
/// type <c>C54</c>). Equivalent to a <see cref="DownloadRequest"/> with
/// <see cref="DownloadRequest.OrderType"/> = <c>"C54"</c>.
/// </summary>
public sealed class C54DownloadRequest : IEbicsRequest<DownloadResult>, IDownloadConvenienceRequest
{
    /// <summary>An optional closed reporting period for the notification.</summary>
    public DateRange? Period { get; init; }

    /// <summary>An optional parsing hook applied to the decrypted order data (typically a ZIP container) before the receipt.</summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }

    /// <inheritdoc />
    string IDownloadConvenienceRequest.OrderType => StatementOrderTypes.NotificationCamt054;

    /// <inheritdoc />
    DateRange? IDownloadConvenienceRequest.Period => Period;

    /// <inheritdoc />
    Func<ReadOnlyMemory<byte>, object?>? IDownloadConvenienceRequest.Parse => Parse;
}
