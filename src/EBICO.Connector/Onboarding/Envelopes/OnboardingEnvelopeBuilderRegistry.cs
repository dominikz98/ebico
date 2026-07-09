using EBICO.Core;

namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// The default <see cref="IOnboardingEnvelopeBuilderRegistry"/>: indexes the registered
/// <see cref="IOnboardingEnvelopeBuilder"/> instances by their <see cref="IOnboardingEnvelopeBuilder.Version"/>.
/// </summary>
internal sealed class OnboardingEnvelopeBuilderRegistry : IOnboardingEnvelopeBuilderRegistry
{
    private readonly Dictionary<EbicsVersion, IOnboardingEnvelopeBuilder> _builders;

    /// <summary>Initializes the registry from the DI-provided builders.</summary>
    /// <param name="builders">The registered version builders.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builders"/> is <see langword="null"/>.</exception>
    public OnboardingEnvelopeBuilderRegistry(IEnumerable<IOnboardingEnvelopeBuilder> builders)
    {
        ArgumentNullException.ThrowIfNull(builders);
        // Last registration wins per version, consistent with DI TryAdd/replace semantics.
        _builders = builders.ToDictionary(static b => b.Version);
    }

    /// <inheritdoc />
    public IOnboardingEnvelopeBuilder Get(EbicsVersion version)
        => _builders.TryGetValue(version, out var builder)
            ? builder
            : throw new EbicsConfigurationException(
                $"No onboarding envelope builder is registered for EBICS version '{version}'.");
}
