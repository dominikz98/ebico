using EBICO.Core;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// Builds and parses the version-specific EBICS onboarding envelopes for a single
/// <see cref="EbicsVersion"/>, encapsulating the three ways the versions differ (public-key
/// representation: RSAKeyValue vs. X.509; order-details: <c>OrderType</c> vs. <c>AdminOrderType</c>;
/// INI order-data namespace: <c>S001</c> vs. <c>S002</c>). Handlers depend only on this abstraction
/// and stay version-agnostic; one implementation exists per supported version, resolved through
/// <see cref="IOnboardingEnvelopeBuilderRegistry"/>.
/// </summary>
public interface IOnboardingEnvelopeBuilder
{
    /// <summary>The EBICS protocol version this builder handles.</summary>
    EbicsVersion Version { get; }

    /// <summary>
    /// Builds the <c>ebicsUnsecuredRequest</c> envelope for the INI order, embedding the compressed,
    /// base64-encoded <c>SignaturePubKeyOrderData</c> for <paramref name="signatureKey"/>.
    /// </summary>
    /// <param name="ctx">The header identifiers.</param>
    /// <param name="signatureKey">The subscriber's public signature key (<c>A00x</c>).</param>
    /// <returns>The ready-to-serialize INI request envelope.</returns>
    IEbicsRequestEnvelope BuildIniRequest(OnboardingHeaderContext ctx, PublicKeyDescriptor signatureKey);

    /// <summary>
    /// Builds the <c>ebicsUnsecuredRequest</c> envelope for the HIA order, embedding the compressed,
    /// base64-encoded <c>HIARequestOrderData</c> for the authentication and encryption keys.
    /// </summary>
    /// <param name="ctx">The header identifiers.</param>
    /// <param name="authenticationKey">The subscriber's public authentication key (<c>X00x</c>).</param>
    /// <param name="encryptionKey">The subscriber's public encryption key (<c>E00x</c>).</param>
    /// <returns>The ready-to-serialize HIA request envelope.</returns>
    IEbicsRequestEnvelope BuildHiaRequest(
        OnboardingHeaderContext ctx, PublicKeyDescriptor authenticationKey, PublicKeyDescriptor encryptionKey);

    /// <summary>
    /// Builds the <c>ebicsNoPubKeyDigestsRequest</c> envelope for the HPB order <b>without</b> its
    /// authentication signature — the caller computes the X002 signature over the serialized envelope
    /// and assigns it to <see cref="IAuthSignedRequestEnvelope.AuthSignature"/>.
    /// </summary>
    /// <param name="ctx">The header identifiers.</param>
    /// <returns>The HPB request envelope, ready for signing.</returns>
    IAuthSignedRequestEnvelope BuildHpbRequest(OnboardingHeaderContext ctx);

    /// <summary>Parses a raw <c>ebicsKeyManagementResponse</c> into the version-agnostic view.</summary>
    /// <param name="responseXml">The response envelope XML.</param>
    /// <returns>The projected response.</returns>
    KeyManagementResponseView ParseResponse(string responseXml);

    /// <summary>
    /// Parses the decrypted, decompressed HPB order-data XML (<c>HPBResponseOrderData</c>) into the
    /// bank's public keys, reading RSAKeyValue (H003/H004) or the X.509 certificate (H005) as
    /// appropriate.
    /// </summary>
    /// <param name="hpbOrderDataXml">The decrypted, decompressed order-data bytes (UTF-8 XML).</param>
    /// <returns>The bank's public keys.</returns>
    BankKeys ParseHpbOrderData(byte[] hpbOrderDataXml);
}
