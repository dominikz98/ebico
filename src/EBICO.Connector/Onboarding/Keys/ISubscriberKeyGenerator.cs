using EBICO.Connector.Keys;

namespace EBICO.Connector.Onboarding.Keys;

/// <summary>
/// Generates the subscriber's client-side EBICS key pairs (signature <c>A00x</c>, authentication
/// <c>X00x</c>, encryption <c>E00x</c>) and stores them in the <see cref="IKeyStore"/> under
/// <see cref="KeyOwner.Subscriber"/>. This is the explicit, one-time key provisioning that must run
/// before the INI/HIA/HPB flows; it is deliberately separate from the <c>Send</c> request pipeline.
/// </summary>
public interface ISubscriberKeyGenerator
{
    /// <summary>Generates and stores the three subscriber key pairs for the configured EBICS version.</summary>
    /// <param name="options">Generation options; <see langword="null"/> uses the defaults.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The generated key versions and fingerprints.</returns>
    /// <exception cref="EbicsConfigurationException">
    /// A subscriber key already exists and <see cref="SubscriberKeyGenerationOptions.Overwrite"/> is
    /// <see langword="false"/>.
    /// </exception>
    Task<SubscriberKeySet> GenerateAsync(
        SubscriberKeyGenerationOptions? options = null, CancellationToken ct = default);
}
