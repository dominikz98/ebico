using EBICO.Core;
using EBICO.Server.ReturnCodes;

namespace EBICO.Server.Pipeline;

/// <summary>
/// The outcome of an <see cref="IEbicsOrderHandler"/>.
/// </summary>
/// <param name="ReturnCode">The return code to report for the handled order.</param>
public readonly record struct EbicsOrderResult(EbicsReturnCode ReturnCode);

/// <summary>
/// Extension point for the pipeline's <em>handle</em> stage: processes one EBICS order type of one
/// protocol version. The skeleton (#25) registers no handlers, so every recognized request is
/// answered with <see cref="EbicsReturnCode.UnsupportedOrderType"/>. Concrete handlers (INI/HIA/HPB,
/// then the transaction engine) are added by the M3/M4 issues.
/// </summary>
public interface IEbicsOrderHandler
{
    /// <summary>The protocol version this handler serves.</summary>
    EbicsVersion Version { get; }

    /// <summary>The order type this handler serves (e.g. <c>"HPB"</c>).</summary>
    string OrderType { get; }

    /// <summary>Handles the order described by <paramref name="context"/>.</summary>
    /// <param name="context">The request context.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The order result.</returns>
    Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default);
}
