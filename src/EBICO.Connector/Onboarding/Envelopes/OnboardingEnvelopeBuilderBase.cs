using System.Security.Cryptography;
using EBICO.Core;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;

namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// Base class for the version-specific onboarding envelope builders. It centralises the flow that
/// is identical across versions — build the order-data graph, serialize it, compress it, wrap it in
/// the request envelope; generate the HPB nonce/timestamp — and delegates the version-specific type
/// construction (envelope, order data, response parsing) to abstract hooks.
/// </summary>
internal abstract class OnboardingEnvelopeBuilderBase : IOnboardingEnvelopeBuilder
{
    /// <summary>The <c>INI</c> admin/order type code.</summary>
    protected const string OrderTypeIni = "INI";

    /// <summary>The <c>HIA</c> admin/order type code.</summary>
    protected const string OrderTypeHia = "HIA";

    /// <summary>The <c>HPB</c> admin/order type code.</summary>
    protected const string OrderTypeHpb = "HPB";

    /// <summary>
    /// The <c>SecurityMedium</c> value for key-management orders. <b>⚠️ Spec-Vorbehalt:</b>
    /// <c>"0000"</c> ("no medium") is the common reading, not verified against the official Annex.
    /// </summary>
    protected const string SecurityMedium = "0000";

    private const int NonceSizeBytes = 16;

    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the base with the time source used for the HPB request timestamp.</summary>
    /// <param name="timeProvider">The time source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    protected OnboardingEnvelopeBuilderBase(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public IEbicsRequestEnvelope BuildIniRequest(OnboardingHeaderContext ctx, PublicKeyDescriptor signatureKey)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(signatureKey);
        var orderData = CompressOrderData(BuildIniOrderData(signatureKey, ctx));
        return BuildUnsecuredEnvelope(ctx, OrderTypeIni, orderData);
    }

    /// <inheritdoc />
    public IEbicsRequestEnvelope BuildHiaRequest(
        OnboardingHeaderContext ctx, PublicKeyDescriptor authenticationKey, PublicKeyDescriptor encryptionKey)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(authenticationKey);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        var orderData = CompressOrderData(BuildHiaOrderData(authenticationKey, encryptionKey, ctx));
        return BuildUnsecuredEnvelope(ctx, OrderTypeHia, orderData);
    }

    /// <inheritdoc />
    public IAuthSignedRequestEnvelope BuildHpbRequest(OnboardingHeaderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return BuildHpbEnvelope(ctx, RandomNumberGenerator.GetBytes(NonceSizeBytes), _timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <inheritdoc />
    public abstract KeyManagementResponseView ParseResponse(string responseXml);

    /// <inheritdoc />
    public abstract BankKeys ParseHpbOrderData(byte[] hpbOrderDataXml);

    /// <summary>Builds the version-specific INI order-data graph (<c>SignaturePubKeyOrderData</c>).</summary>
    /// <param name="signatureKey">The subscriber signature key.</param>
    /// <param name="ctx">The header identifiers (for PartnerID/UserID).</param>
    /// <returns>The order-data graph to serialize.</returns>
    protected abstract object BuildIniOrderData(PublicKeyDescriptor signatureKey, OnboardingHeaderContext ctx);

    /// <summary>Builds the version-specific HIA order-data graph (<c>HIARequestOrderData</c>).</summary>
    /// <param name="authenticationKey">The subscriber authentication key.</param>
    /// <param name="encryptionKey">The subscriber encryption key.</param>
    /// <param name="ctx">The header identifiers (for PartnerID/UserID).</param>
    /// <returns>The order-data graph to serialize.</returns>
    protected abstract object BuildHiaOrderData(
        PublicKeyDescriptor authenticationKey, PublicKeyDescriptor encryptionKey, OnboardingHeaderContext ctx);

    /// <summary>Wraps the compressed order data in the version-specific <c>ebicsUnsecuredRequest</c> envelope.</summary>
    /// <param name="ctx">The header identifiers.</param>
    /// <param name="orderType">The order type code (INI/HIA).</param>
    /// <param name="compressedOrderData">The compressed order-data bytes.</param>
    /// <returns>The request envelope.</returns>
    protected abstract IEbicsRequestEnvelope BuildUnsecuredEnvelope(
        OnboardingHeaderContext ctx, string orderType, byte[] compressedOrderData);

    /// <summary>Builds the version-specific <c>ebicsNoPubKeyDigestsRequest</c> (HPB) envelope, unsigned.</summary>
    /// <param name="ctx">The header identifiers.</param>
    /// <param name="nonce">The 16-byte request nonce.</param>
    /// <param name="timestamp">The request timestamp (UTC).</param>
    /// <returns>The HPB request envelope, ready for signing.</returns>
    protected abstract IAuthSignedRequestEnvelope BuildHpbEnvelope(
        OnboardingHeaderContext ctx, byte[] nonce, DateTime timestamp);

    private static byte[] CompressOrderData(object orderData)
        => EbicsCompression.Compress(EbicsXmlSerializer.SerializeOrderData(orderData));
}
