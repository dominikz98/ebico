using EBICO.Core;

namespace EBICO.Connector.Upload.Envelopes;

/// <summary>Resolves the <see cref="IUploadEnvelopeBuilder"/> for a given EBICS version.</summary>
internal interface IUploadEnvelopeBuilderRegistry
{
    /// <summary>Gets the builder for <paramref name="version"/>.</summary>
    /// <param name="version">The EBICS version.</param>
    /// <returns>The matching builder.</returns>
    /// <exception cref="EbicsConfigurationException">No builder is registered for <paramref name="version"/>.</exception>
    IUploadEnvelopeBuilder Get(EbicsVersion version);
}
