using System.Text;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Download;

/// <summary>
/// Shared steps for the download handler: retrieving the subscriber keys and the serialize → transport
/// → return-response-XML exchange (mirrors <c>UploadSupport</c>). A download only needs subscriber keys
/// — the response is E002-encrypted for the subscriber and the requests are X002-signed — so there is
/// no bank-key retrieval here.
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

    /// <summary>Serializes the envelope, sends it via the transport and returns the response XML.</summary>
    /// <param name="envelope">The request envelope.</param>
    /// <param name="ctx">The execution context.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The response XML.</returns>
    public static async Task<string> ExchangeAsync(
        IEbicsRequestEnvelope envelope, EbicsContext ctx, CancellationToken ct)
    {
        var payload = EbicsXmlSerializer.SerializeToUtf8Bytes(envelope);
        var response = await ctx.Transport
            .SendAsync(new EbicsHttpRequest { Payload = payload }, ct)
            .ConfigureAwait(false);
        return Encoding.UTF8.GetString(response.Payload.Span);
    }
}
