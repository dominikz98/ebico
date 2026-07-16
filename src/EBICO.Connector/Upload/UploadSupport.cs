using System.Text;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Upload;

/// <summary>
/// Shared steps for the upload handler: retrieving subscriber and bank keys, and the serialize →
/// transport → return-response-XML exchange (mirrors <c>OnboardingSupport</c>).
/// </summary>
internal static class UploadSupport
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
                "onboarding flows (INI/HIA) before uploading.");

    /// <summary>Retrieves a bank key or throws when the bank keys have not been fetched yet (HPB).</summary>
    /// <param name="ctx">The execution context.</param>
    /// <param name="purpose">The key purpose.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The bank key material.</returns>
    /// <exception cref="EbicsConfigurationException">The key is absent from the store.</exception>
    public static async Task<RsaKeyMaterial> RequireBankKeyAsync(
        EbicsContext ctx, KeyPurpose purpose, CancellationToken ct)
        => await ctx.Keys.GetAsync(KeyOwner.Bank, purpose, ct).ConfigureAwait(false)
            ?? throw new EbicsConfigurationException(
                $"No bank {purpose} key is present. Run the HPB onboarding flow to fetch and store the " +
                "bank keys before uploading.");

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
