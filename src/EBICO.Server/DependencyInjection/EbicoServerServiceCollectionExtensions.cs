using EBICO.Server;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for the EBICO server (emulator) host.
/// </summary>
public static class EbicoServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EBICO server: the options, the request pipeline, the verify/handle extension
    /// points with their skeleton defaults (a no-op verifier and no order handlers), the error
    /// mapper, the response factory, the default in-memory state store and the master-data manager
    /// (<see cref="EBICO.Server.State.IMasterDataManager"/>) that backs the admin API. All concrete
    /// services are registered with <c>TryAdd*</c>, so a caller can override any of them before calling this.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration of the server options.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddEbicoServer(
        this IServiceCollection services,
        Action<EbicoServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<EbicoServerOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton<IEbicsStateStore, InMemoryEbicsStateStore>();
        services.TryAddSingleton<IMasterDataManager, MasterDataManager>();
        services.TryAddSingleton<IEbicsRequestVerifier, NoOpEbicsRequestVerifier>();
        services.TryAddSingleton<IEbicsErrorMapper, EbicsErrorMapper>();
        services.TryAddSingleton<EbicsResponseFactory>();
        services.TryAddSingleton<IEbicsOrderHandlerResolver, EbicsOrderHandlerResolver>();
        services.TryAddSingleton<IEbicsRequestPipeline, EbicsRequestPipeline>();

        // No default IEbicsOrderHandler registrations: the skeleton resolves no handler and
        // answers recognized requests with EBICS_UNSUPPORTED_ORDER_TYPE. M3/M4 issues add handlers.
        return services;
    }
}
