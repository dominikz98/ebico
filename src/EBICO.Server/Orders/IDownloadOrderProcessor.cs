using EBICO.Core;
using EBICO.Server.State;

namespace EBICO.Server.Orders;

/// <summary>
/// The request handed to an <see cref="IDownloadOrderProcessor"/> to generate download content on demand
/// (issue #40): the requesting subscriber, the protocol version, the resolved classical order type and the
/// optional reporting period.
/// </summary>
/// <param name="Subscriber">The subscriber requesting the download.</param>
/// <param name="Version">The protocol version the download runs under.</param>
/// <param name="EffectiveOrderType">The resolved classical order-type code (e.g. <c>"C53"</c>), not the generic <c>FDL</c>/<c>BTD</c>.</param>
/// <param name="DateRange">The requested reporting period, or <see langword="null"/> when the request left it open.</param>
public readonly record struct DownloadOrderRequest(
    SubscriberKeyRef Subscriber,
    EbicsVersion Version,
    string EffectiveOrderType,
    DateRange? DateRange);

/// <summary>
/// Generates download content on demand for order types the emulator can synthesise (issue #40) — the
/// download counterpart of <see cref="IUploadOrderProcessor"/>. The download engine calls it during
/// initialisation when no pre-seeded payload is queued for the resolved order type; a returned byte array is
/// the plaintext the engine then compresses, E002-encrypts and segments, and <see langword="null"/> declines
/// (the engine then reports <c>EBICS_NO_DOWNLOAD_DATA_AVAILABLE</c>).
/// </summary>
/// <remarks>
/// The default registration is the <see cref="StatementDownloadProcessor"/> (account statements/reports); a
/// caller can substitute or extend it via <c>TryAddSingleton</c> before <c>AddEbicoServer</c>. Pre-seeded
/// data (admin API / re-enqueue on a negative receipt) always takes precedence over generation.
/// </remarks>
public interface IDownloadOrderProcessor
{
    /// <summary>Whether this processor can generate content for the given resolved order type.</summary>
    /// <param name="effectiveOrderType">The resolved classical order-type code, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <see cref="GenerateAsync"/> should be invoked for this order type.</returns>
    bool CanProcess(string? effectiveOrderType);

    /// <summary>
    /// Generates the plaintext order data for the request, or <see langword="null"/> to decline. Only called
    /// when <see cref="CanProcess"/> returned <see langword="true"/>.
    /// </summary>
    /// <param name="request">The resolved download request.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The generated plaintext order data, or <see langword="null"/> when nothing is produced.</returns>
    Task<byte[]?> GenerateAsync(DownloadOrderRequest request, CancellationToken ct = default);
}
