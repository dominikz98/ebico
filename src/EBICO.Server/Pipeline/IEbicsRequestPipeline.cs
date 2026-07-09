namespace EBICO.Server.Pipeline;

/// <summary>
/// Orchestrates the EBICS request pipeline — Parse → Version-Dispatch → Verify → Handle → Respond —
/// on a raw request body, producing a serialized <c>ebicsResponse</c>. Deliberately HTTP-free so it
/// can be exercised without a web host.
/// </summary>
public interface IEbicsRequestPipeline
{
    /// <summary>Processes a raw EBICS request and produces the response bytes.</summary>
    /// <param name="requestXml">The raw request XML.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The pipeline result carrying the serialized response.</returns>
    Task<EbicsPipelineResult> ProcessAsync(string requestXml, CancellationToken ct = default);
}
