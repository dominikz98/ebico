using EBICO.Core;

namespace EBICO.Connector.Download.Envelopes;

/// <summary>Resolves the <see cref="IDownloadEnvelopeBuilder"/> for a given EBICS version.</summary>
internal interface IDownloadEnvelopeBuilderRegistry
{
    /// <summary>Gets the builder for <paramref name="version"/>.</summary>
    /// <param name="version">The EBICS version.</param>
    /// <returns>The matching builder.</returns>
    /// <exception cref="EbicsConfigurationException">No builder is registered for <paramref name="version"/>.</exception>
    IDownloadEnvelopeBuilder Get(EbicsVersion version);
}
