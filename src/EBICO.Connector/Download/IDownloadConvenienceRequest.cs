using EBICO.Core;

namespace EBICO.Connector.Download;

/// <summary>
/// Internal shape shared by the download convenience requests (<see cref="StaDownloadRequest"/> etc.):
/// it projects the request onto the generic download inputs (a classical order type plus the optional
/// reporting period and parsing hook) so a single <see cref="DownloadConvenienceHandlerBase{TRequest}"/>
/// can drive them all.
/// </summary>
internal interface IDownloadConvenienceRequest
{
    /// <summary>The classical order type code (e.g. <c>"STA"</c>, <c>"HTD"</c>).</summary>
    string OrderType { get; }

    /// <summary>An optional closed reporting period.</summary>
    DateRange? Period { get; }

    /// <summary>An optional parsing hook applied to the decrypted order data before the receipt.</summary>
    Func<ReadOnlyMemory<byte>, object?>? Parse { get; }
}
