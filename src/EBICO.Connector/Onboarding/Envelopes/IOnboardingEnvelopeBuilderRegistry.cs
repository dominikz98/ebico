using EBICO.Core;

namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// Resolves the <see cref="IOnboardingEnvelopeBuilder"/> for a given <see cref="EbicsVersion"/>,
/// mirroring the <c>EbicsVersions</c>/<c>KeyVersions</c> registry style.
/// </summary>
public interface IOnboardingEnvelopeBuilderRegistry
{
    /// <summary>Returns the builder for <paramref name="version"/>.</summary>
    /// <param name="version">The target EBICS protocol version.</param>
    /// <returns>The matching builder.</returns>
    /// <exception cref="EbicsConfigurationException">No builder is registered for the version.</exception>
    IOnboardingEnvelopeBuilder Get(EbicsVersion version);
}
