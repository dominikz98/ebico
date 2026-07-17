namespace EBICO.Connector.Quickstart;

/// <summary>
/// The per-step outcome of <see cref="QuickstartRunner.RunAsync"/>. <see cref="Success"/> is
/// <see langword="true"/> only when every step reported a business success (note that a successful
/// download ends with <c>011000</c>, not <c>000000</c>).
/// </summary>
/// <param name="Success">Whether every step succeeded.</param>
/// <param name="IniReturnCode">The INI return code.</param>
/// <param name="HiaReturnCode">The HIA return code.</param>
/// <param name="HpbReturnCode">The HPB return code.</param>
/// <param name="UploadReturnCode">The CCT upload return code.</param>
/// <param name="UploadTransactionId">The server-assigned upload transaction id, or <see langword="null"/>.</param>
/// <param name="DownloadReturnCode">The C53 download return code (<c>011000</c> on success).</param>
/// <param name="DownloadSegments">The number of order-data segments the download returned.</param>
public sealed record QuickstartResult(
    bool Success,
    string IniReturnCode,
    string HiaReturnCode,
    string HpbReturnCode,
    string UploadReturnCode,
    string? UploadTransactionId,
    string DownloadReturnCode,
    int DownloadSegments);
