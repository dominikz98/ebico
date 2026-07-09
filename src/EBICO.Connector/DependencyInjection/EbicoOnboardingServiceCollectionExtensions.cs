using EBICO.Connector;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Envelopes;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Connector.Onboarding.Letter;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for the EBICO connector onboarding flows (INI/HIA/HPB). Call this after
/// <see cref="EbicoConnectorServiceCollectionExtensions.AddEbicoConnector"/>, which provides the
/// connection, key store and transport the handlers depend on.
/// </summary>
public static class EbicoOnboardingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the onboarding request handlers (INI/HIA/HPB), the per-version envelope builders and
    /// their registry, the subscriber key generator, the initialization-letter renderer (text + PDF)
    /// and a default <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddEbicoOnboarding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        // Version-specific envelope builders + registry.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IOnboardingEnvelopeBuilder, H003OnboardingEnvelopeBuilder>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IOnboardingEnvelopeBuilder, H004OnboardingEnvelopeBuilder>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IOnboardingEnvelopeBuilder, H005OnboardingEnvelopeBuilder>());
        services.TryAddSingleton<IOnboardingEnvelopeBuilderRegistry, OnboardingEnvelopeBuilderRegistry>();

        // Explicit key provisioning and the initialization letter (text + PDF via QuestPDF).
        services.TryAddSingleton<ISubscriberKeyGenerator, SubscriberKeyGenerator>();
        services.TryAddSingleton<IInitializationLetterRenderer, PdfInitializationLetterRenderer>();

        // The three onboarding handlers, resolved by the client dispatch per request type.
        services.TryAddSingleton<IEbicsRequestHandler<IniRequest, IniResult>, IniRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<HiaRequest, HiaResult>, HiaRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<HpbRequest, HpbResult>, HpbRequestHandler>();

        return services;
    }
}
