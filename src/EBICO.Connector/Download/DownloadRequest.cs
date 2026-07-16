using EBICO.Core;
using EBICO.Core.Btf;

namespace EBICO.Connector.Download;

/// <summary>
/// A generic EBICS download request: fetches order data (e.g. an account statement or report) from the
/// bank in the three-phase download transaction (initialisation → transfer → receipt). The connector
/// collects the segments, reassembles and E002-decrypts the ciphertext with the subscriber's private
/// encryption key, decompresses it and acknowledges receipt.
/// </summary>
/// <remarks>
/// <para>
/// The order is identified in a version-appropriate way. On H005 a statement/report is requested as
/// <c>BTD</c> with a <see cref="Btf"/> (resolved from <see cref="OrderType"/> when not supplied), while
/// administrative downloads (e.g. <c>HTD</c>) keep their classical order type. On H003/H004 either a
/// classical order type (<see cref="OrderType"/>, e.g. <c>"STA"</c>) is submitted directly, or the
/// generic <c>FDL</c> file download is used when a <see cref="FileFormat"/> is given.
/// </para>
/// <para>
/// For the common statement/report and status/protocol orders prefer the convenience requests
/// (<see cref="StaDownloadRequest"/> etc.). The subscriber must be fully onboarded (INI/HIA) so its own
/// private E002 encryption key is available in the key store.
/// </para>
/// </remarks>
public sealed class DownloadRequest : IEbicsRequest<DownloadResult>
{
    /// <summary>
    /// The order type: a classical code such as <c>"STA"</c>/<c>"C53"</c>/<c>"HTD"</c> (H003/H004 submit
    /// it directly; H005 resolves statement codes to a BTF when <see cref="Btf"/> is not set and keeps
    /// administrative codes as the <c>AdminOrderType</c>), or <see langword="null"/> when a
    /// <see cref="Btf"/> (H005) or <see cref="FileFormat"/> (H003/H004 FDL) is supplied instead.
    /// </summary>
    public string? OrderType { get; init; }

    /// <summary>The H005 business transaction format placed in <c>BTDOrderParams/Service</c>; ignored for H003/H004.</summary>
    public BusinessTransactionFormat? Btf { get; init; }

    /// <summary>The H003/H004 <c>FDLOrderParams/FileFormat</c> value for the generic file download; ignored for H005.</summary>
    public string? FileFormat { get; init; }

    /// <summary>An optional closed reporting period for the download (placed in the version-specific order params).</summary>
    public DateRange? Period { get; init; }

    /// <summary>
    /// An optional parsing hook applied to the decrypted, decompressed order data <b>before</b> the
    /// receipt is acknowledged. Its result is exposed via <see cref="DownloadResult.Parsed"/>
    /// (typed access through <see cref="DownloadResult.ParsedAs{T}"/>). When the hook throws, the
    /// connector sends a <em>negative</em> receipt (so the server re-provides the data) and rethrows.
    /// </summary>
    public Func<ReadOnlyMemory<byte>, object?>? Parse { get; init; }
}
