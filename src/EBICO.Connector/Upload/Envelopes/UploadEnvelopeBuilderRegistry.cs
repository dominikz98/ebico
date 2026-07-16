using EBICO.Core;

namespace EBICO.Connector.Upload.Envelopes;

/// <summary>
/// The default <see cref="IUploadEnvelopeBuilderRegistry"/>: indexes the registered
/// <see cref="IUploadEnvelopeBuilder"/> instances by their <see cref="IUploadEnvelopeBuilder.Version"/>.
/// </summary>
internal sealed class UploadEnvelopeBuilderRegistry : IUploadEnvelopeBuilderRegistry
{
    private readonly Dictionary<EbicsVersion, IUploadEnvelopeBuilder> _builders;

    /// <summary>Initializes the registry from the DI-provided builders.</summary>
    /// <param name="builders">The registered version builders.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builders"/> is <see langword="null"/>.</exception>
    public UploadEnvelopeBuilderRegistry(IEnumerable<IUploadEnvelopeBuilder> builders)
    {
        ArgumentNullException.ThrowIfNull(builders);
        // Last registration wins per version, consistent with DI TryAdd/replace semantics.
        _builders = builders.ToDictionary(static b => b.Version);
    }

    /// <inheritdoc />
    public IUploadEnvelopeBuilder Get(EbicsVersion version)
        => _builders.TryGetValue(version, out var builder)
            ? builder
            : throw new EbicsConfigurationException(
                $"No upload envelope builder is registered for EBICS version '{version}'.");
}
