namespace EBICO.Connector.Download;

/// <summary>
/// The result of a successful EBICS download transaction: the server-assigned transaction id, the
/// number of order-data segments received, the decrypted and decompressed order data, and — when a
/// parsing hook was supplied — its parsed representation.
/// </summary>
public sealed class DownloadResult
{
    /// <summary>The transaction id assigned by the server, as an uppercase hex string.</summary>
    public required string TransactionId { get; init; }

    /// <summary>The number of order-data segments received (≥ 1).</summary>
    public required int NumSegments { get; init; }

    /// <summary>
    /// The decrypted, decompressed order data (the business payload; for statement/report downloads
    /// this is typically a ZIP container).
    /// </summary>
    public required ReadOnlyMemory<byte> OrderData { get; init; }

    /// <summary>
    /// The value produced by the request's parsing hook, or <see langword="null"/> when no hook was
    /// supplied. Use <see cref="ParsedAs{T}"/> for typed access.
    /// </summary>
    public object? Parsed { get; init; }

    /// <summary>Returns <see cref="Parsed"/> cast to <typeparamref name="T"/>, or the default when it is absent or of a different type.</summary>
    /// <typeparam name="T">The expected parsed type (the return type of the request's <c>Parse</c> hook).</typeparam>
    /// <returns>The parsed value as <typeparamref name="T"/>, or <see langword="default"/>.</returns>
    public T? ParsedAs<T>() => Parsed is T value ? value : default;
}
