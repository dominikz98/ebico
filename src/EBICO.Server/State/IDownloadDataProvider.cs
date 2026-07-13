using EBICO.Core;

namespace EBICO.Server.State;

/// <summary>
/// Identifies which order data a download initialisation is asking for: the protocol
/// <see cref="Version"/>, the requesting <see cref="Subscriber"/> and the download
/// <see cref="OrderType"/> (<c>"FDL"</c> for H003/H004, <c>"BTD"</c> for H005).
/// </summary>
/// <param name="Version">The protocol version the download runs under.</param>
/// <param name="Subscriber">The subscriber requesting the download.</param>
/// <param name="OrderType">The download order type.</param>
public readonly record struct DownloadDataRequest(EbicsVersion Version, SubscriberKeyRef Subscriber, string OrderType);

/// <summary>
/// Supplies the plaintext order data the server hands out for a download (issue #33): the
/// "Datenbereitstellung serverseitig". A download initialisation dequeues the next available payload
/// for the (subscriber, order type); when none is available the engine reports
/// <c>EBICS_NO_DOWNLOAD_DATA_AVAILABLE</c> (090005). A negative receipt re-enqueues the payload so it
/// can be downloaded again.
/// </summary>
/// <remarks>
/// The default registration is the in-memory <see cref="InMemoryDownloadDataProvider"/>, seedable via
/// the admin API (or directly in tests). A real order-data source can be substituted via
/// <c>TryAddSingleton</c> before <c>AddEbicoServer</c>. Order data is provided as the raw plaintext
/// (before compression/encryption); the engine performs compress → E002-encrypt → segment.
/// </remarks>
public interface IDownloadDataProvider
{
    /// <summary>
    /// Enqueues <paramref name="orderData"/> as an available download for <paramref name="subscriber"/>
    /// and <paramref name="orderType"/>. Payloads are served in FIFO order.
    /// </summary>
    /// <param name="subscriber">The subscriber the download is made available to.</param>
    /// <param name="orderType">The download order type (e.g. <c>"FDL"</c>/<c>"BTD"</c>).</param>
    /// <param name="orderData">The raw plaintext order data (before compression/encryption).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the payload has been enqueued.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="orderType"/> or <paramref name="orderData"/> is <see langword="null"/>.</exception>
    Task EnqueueAsync(SubscriberKeyRef subscriber, string orderType, byte[] orderData, CancellationToken ct = default);

    /// <summary>
    /// Removes and returns the next available payload for the (subscriber, order type) in
    /// <paramref name="request"/>, or <see langword="null"/> when none is available.
    /// </summary>
    /// <param name="request">The download request (subscriber + order type).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The next payload, or <see langword="null"/> when none is available.</returns>
    Task<byte[]?> TryDequeueAsync(DownloadDataRequest request, CancellationToken ct = default);

    /// <summary>Returns the number of payloads currently available for <paramref name="subscriber"/> and <paramref name="orderType"/>.</summary>
    /// <param name="subscriber">The subscriber to query.</param>
    /// <param name="orderType">The download order type.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The number of pending payloads.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="orderType"/> is <see langword="null"/>.</exception>
    Task<int> CountAsync(SubscriberKeyRef subscriber, string orderType, CancellationToken ct = default);
}
