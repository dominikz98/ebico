using EBICO.Core;

namespace EBICO.Server.Pipeline;

/// <summary>
/// The outcome of the request pipeline: the serialized <c>ebicsResponse</c> bytes and the
/// version they were produced in.
/// </summary>
/// <param name="Body">The serialized response envelope (UTF-8, no BOM).</param>
/// <param name="Version">The protocol version the response was produced in.</param>
public readonly record struct EbicsPipelineResult(byte[] Body, EbicsVersion Version)
{
    /// <summary>The HTTP <c>Content-Type</c> the response body should be served with.</summary>
    public string ContentType => "text/xml; charset=utf-8";
}
