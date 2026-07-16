using EBICO.Connector;
using EBICO.Connector.Download;
using EBICO.Connector.Download.Envelopes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for the EBICO connector download API (generic <see cref="DownloadRequest"/> plus
/// the statement/report convenience requests STA/VMK/C53/C52/C54 and the status/protocol convenience
/// requests HAC/HTD/HKD/HAA/HPD/PTK). Call this after
/// <see cref="EbicoConnectorServiceCollectionExtensions.AddEbicoConnector"/>, which provides the
/// connection, key store and transport the handlers depend on.
/// </summary>
public static class EbicoDownloadServiceCollectionExtensions
{
    /// <summary>
    /// Registers the download request handlers (generic + statement/report + status/protocol), the
    /// per-version envelope builders and their registry, and the shared download executor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddEbicoDownload(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Version-specific envelope builders + registry.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDownloadEnvelopeBuilder, H003DownloadEnvelopeBuilder>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDownloadEnvelopeBuilder, H004DownloadEnvelopeBuilder>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDownloadEnvelopeBuilder, H005DownloadEnvelopeBuilder>());
        services.TryAddSingleton<IDownloadEnvelopeBuilderRegistry, DownloadEnvelopeBuilderRegistry>();

        // The shared transaction executor used by every download handler.
        services.TryAddSingleton<DownloadExecutor>();

        // The generic download handler, resolved by the client dispatch per request type.
        services.TryAddSingleton<IEbicsRequestHandler<DownloadRequest, DownloadResult>, DownloadRequestHandler>();

        // Statement/report convenience handlers (STA/VMK/C53/C52/C54).
        services.TryAddSingleton<IEbicsRequestHandler<StaDownloadRequest, DownloadResult>, StaDownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<VmkDownloadRequest, DownloadResult>, VmkDownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<C53DownloadRequest, DownloadResult>, C53DownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<C52DownloadRequest, DownloadResult>, C52DownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<C54DownloadRequest, DownloadResult>, C54DownloadRequestHandler>();

        // Status/protocol convenience handlers (HTD/HKD/HAA/HPD/HAC/PTK).
        services.TryAddSingleton<IEbicsRequestHandler<HtdDownloadRequest, DownloadResult>, HtdDownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<HkdDownloadRequest, DownloadResult>, HkdDownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<HaaDownloadRequest, DownloadResult>, HaaDownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<HpdDownloadRequest, DownloadResult>, HpdDownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<HacDownloadRequest, DownloadResult>, HacDownloadRequestHandler>();
        services.TryAddSingleton<IEbicsRequestHandler<PtkDownloadRequest, DownloadResult>, PtkDownloadRequestHandler>();

        return services;
    }
}
