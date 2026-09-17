using System.Text;
using EBICO.Connector.Keys;
using EBICO.Connector.Security;
using EBICO.Connector.Transport;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Download;

/// <summary>
/// Shared steps for the download handler: retrieving the subscriber keys and the serialize → transport
/// → verify → return-response-XML exchange (mirrors <c>UploadSupport</c>). A download needs no bank
/// key to <em>build</em> its requests — the response is E002-encrypted for the subscriber and the
/// requests are X002-signed with the subscriber's own key — but the bank's authentication key is read
/// (by <see cref="ResponseSignatureVerifier"/>) to verify the signature on the way back.
/// </summary>
internal static class DownloadSupport
{
    /// <summary>Retrieves a subscriber key or throws when it has not been generated yet.</summary>
    /// <param name="ctx">The execution context.</param>
    /// <param name="purpose">The key purpose.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The subscriber key material.</returns>
    /// <exception cref="EbicsConfigurationException">The key is absent from the store.</exception>
    public static async Task<RsaKeyMaterial> RequireSubscriberKeyAsync(
        EbicsContext ctx, KeyPurpose purpose, CancellationToken ct)
        => await ctx.Keys.GetAsync(KeyOwner.Subscriber, purpose, ct).ConfigureAwait(false)
            ?? throw new EbicsConfigurationException(
                $"No subscriber {purpose} key is present. Generate the subscriber keys and complete the " +
                "onboarding flows (INI/HIA) before downloading.");

    /// <summary>
    /// Serializes the envelope, sends it via the transport, verifies the bank's X002 signature on the
    /// response and returns the response XML.
    /// </summary>
    /// <param name="envelope">The request envelope.</param>
    /// <param name="ctx">The execution context.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The verified response XML.</returns>
    /// <exception cref="EbicsResponseSignatureException">
    /// The response is unsigned or its signature does not verify (unless verification is switched off
    /// via <c>EbicsConnectionOptions.VerifyResponseSignature</c>).
    /// </exception>
    public static async Task<string> ExchangeAsync(
        IEbicsRequestEnvelope envelope, EbicsContext ctx, CancellationToken ct)
    {
        var payload = EbicsXmlSerializer.SerializeToUtf8Bytes(envelope);
        var response = await ctx.Transport
            .SendAsync(new EbicsHttpRequest { Payload = payload }, ct)
            .ConfigureAwait(false);
        var responseXml = Encoding.UTF8.GetString(response.Payload.Span);

        // The bank signs its ebicsResponse; verifying that signature here is what makes the return code
        // and the payload below attributable to the bank rather than to whatever answered on the wire.
        await ResponseSignatureVerifier.VerifyAsync(responseXml, ctx, ct).ConfigureAwait(false);
        return responseXml;
    }
}
