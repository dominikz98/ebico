using EBICO.Core.Domain;
using EBICO.Core.Versioning;

namespace EBICO.Server.Pipeline;

/// <summary>
/// Extension point for the pipeline's <em>respond</em> stage: turns the built response envelope into
/// the octets that go on the wire, optionally attaching the bank's EBICS authentication signature
/// (<c>AuthSignature</c>, key version X002) — the mirror image of the inbound
/// <see cref="IEbicsRequestVerifier"/>.
/// </summary>
/// <remarks>
/// The signer owns the serialisation because signing is a serialise → sign → re-serialise cycle: the
/// signature is computed over the canonical form of the <em>unsigned</em> envelope and then written
/// back into it. Having one seam produce the final bytes keeps the pipeline (and the raw-message
/// capture that records them) from ever seeing a half-signed intermediate.
/// </remarks>
public interface IEbicsResponseSigner
{
    /// <summary>
    /// Serializes <paramref name="response"/> to the octets to return to the client, signing it when
    /// the implementation and the envelope shape allow it.
    /// </summary>
    /// <param name="response">The response envelope to serialize (and possibly sign).</param>
    /// <param name="host">
    /// The host id the response is sent on behalf of, taken from the request header; <see langword="null"/>
    /// when the request never parsed far enough to carry one (malformed XML). A signer that needs the
    /// bank's key pair returns the envelope unsigned in that case — without a host there is no bank
    /// identity to sign as.
    /// </param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The serialized (and possibly signed) response as UTF-8 bytes.</returns>
    Task<byte[]> SerializeAsync(IEbicsResponseEnvelope response, HostId? host, CancellationToken ct = default);
}
