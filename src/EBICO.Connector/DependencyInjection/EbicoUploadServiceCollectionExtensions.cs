using EBICO.Connector;
using EBICO.Connector.Upload;
using EBICO.Connector.Upload.Envelopes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for the EBICO connector upload API (generic <see cref="UploadRequest"/> plus
/// the SEPA payment convenience requests CCT/CDD/CDB/CIP). Call this after
/// <see cref="EbicoConnectorServiceCollectionExtensions.AddEbicoConnector"/>, which provides the
/// connection, key store and transport the handlers depend on.
/// </summary>
public static class EbicoUploadServiceCollectionExtensions
{
    /// <summary>
    /// Registers the upload request handlers (generic + CCT/CDD/CDB/CIP), the per-version envelope
    /// builders and their registry, and the shared upload executor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddEbicoUpload(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Version-specific envelope builders + registry.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IUploadEnvelopeBuilder, H003UploadEnvelopeBuilder>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IUploadEnvelopeBuilder, H004UploadEnvelopeBuilder>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IUploadEnvelopeBuilder, H005UploadEnvelopeBuilder>());
        services.TryAddSingleton<IUploadEnvelopeBuilderRegistry, UploadEnvelopeBuilderRegistry>();

        // The shared transaction executor used by every upload handler.
        services.TryAddSingleton<UploadExecutor>();

        // The generic upload handler and the SEPA payment convenience handlers, resolved by the client
        // dispatch per request type.
        services.TryAddSingleton<IEbicsRequestHandler<UploadRequest, UploadResult>, UploadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<CctUploadRequest, UploadResult>, CctUploadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<CddUploadRequest, UploadResult>, CddUploadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<CdbUploadRequest, UploadResult>, CdbUploadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<CipUploadRequest, UploadResult>, CipUploadRequestHandler>();

        return services;
    }
}
