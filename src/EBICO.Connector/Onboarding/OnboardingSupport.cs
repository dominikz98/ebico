using System.Security.Cryptography.X509Certificates;
using System.Text;
using EBICO.Connector.Keys;
using EBICO.Connector.Onboarding.Envelopes;
using EBICO.Connector.Transport;
using EBICO.Core.Crypto;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Onboarding;

/// <summary>
/// Shared steps used by the three onboarding handlers: projecting the connection into a header
/// context, retrieving subscriber keys, building the public-key descriptor (with an on-the-fly
/// self-signed certificate for the certificate-based H005 procedure), and the serialize → transport
/// → return-response-XML exchange.
/// </summary>
internal static class OnboardingSupport
{
    /// <summary>Projects the connection identifiers into an <see cref="OnboardingHeaderContext"/>.</summary>
    public static OnboardingHeaderContext Header(EbicsContext ctx)
        => new(ctx.Connection.HostId.Value, ctx.Connection.PartnerId.Value, ctx.Connection.UserId.Value);

    /// <summary>Retrieves a subscriber key or throws when it has not been generated yet.</summary>
    /// <exception cref="EbicsConfigurationException">The key is absent from the store.</exception>
    public static async Task<RsaKeyMaterial> RequireSubscriberKeyAsync(
        EbicsContext ctx, KeyPurpose purpose, CancellationToken ct)
        => await ctx.Keys.GetAsync(KeyOwner.Subscriber, purpose, ct).ConfigureAwait(false)
            ?? throw new EbicsConfigurationException(
                $"No subscriber {purpose} key is present. Generate the subscriber keys with " +
                "ISubscriberKeyGenerator before running the onboarding flows.");

    /// <summary>
    /// Builds a <see cref="PublicKeyDescriptor"/> for order data. For certificate-based procedures
    /// (H005) it creates a short-lived self-signed certificate from the (private) key material; for
    /// pure-key procedures (H003/H004) the certificate stays <see langword="null"/>.
    /// </summary>
    public static PublicKeyDescriptor Descriptor(
        EbicsContext ctx, RsaKeyMaterial material, KeyVersion version, KeyPurpose purpose, TimeProvider timeProvider)
    {
        X509Certificate2? certificate = null;
        if (CertificateRequirements.For(ctx.Connection.Version) == CertificateRequirement.Required)
        {
            var now = timeProvider.GetUtcNow();
            certificate = SelfSignedCertificateFactory.Create(
                material, purpose, $"CN={ctx.Connection.UserId.Value}", now.AddMinutes(-5), now.AddYears(2));
        }

        return new PublicKeyDescriptor(material.ToPublicOnly(), version.Value, certificate);
    }

    /// <summary>Serializes the envelope, sends it via the transport and returns the response XML.</summary>
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
