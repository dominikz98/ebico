using EBICO.Core;

namespace EBICO.Connector.Download.Envelopes;

/// <summary>
/// The default <see cref="IDownloadEnvelopeBuilderRegistry"/>: indexes the registered
/// <see cref="IDownloadEnvelopeBuilder"/> instances by their <see cref="IDownloadEnvelopeBuilder.Version"/>.
/// </summary>
internal sealed class DownloadEnvelopeBuilderRegistry : IDownloadEnvelopeBuilderRegistry
{
    private readonly Dictionary<EbicsVersion, IDownloadEnvelopeBuilder> _builders;

    /// <summary>Initializes the registry from the DI-provided builders.</summary>
    /// <param name="builders">The registered version builders.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builders"/> is <see langword="null"/>.</exception>
    public DownloadEnvelopeBuilderRegistry(IEnumerable<IDownloadEnvelopeBuilder> builders)
    {
        ArgumentNullException.ThrowIfNull(builders);
        // Last registration wins per version, consistent with DI TryAdd/replace semantics.
        _builders = builders.ToDictionary(static b => b.Version);
    }

    /// <inheritdoc />
    public IDownloadEnvelopeBuilder Get(EbicsVersion version)
        => _builders.TryGetValue(version, out var builder)
            ? builder
            : throw new EbicsConfigurationException(
                $"No download envelope builder is registered for EBICS version '{version}'.");
}
