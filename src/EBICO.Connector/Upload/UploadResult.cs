namespace EBICO.Connector.Upload;

/// <summary>
/// The result of a successful EBICS upload transaction: the server-assigned transaction id and the
/// number of order-data segments transferred.
/// </summary>
public sealed class UploadResult
{
    /// <summary>The transaction id assigned by the server, as an uppercase hex string.</summary>
    public required string TransactionId { get; init; }

    /// <summary>The number of order-data segments transferred (≥ 1).</summary>
    public required int NumSegments { get; init; }
}
