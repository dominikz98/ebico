using EBICO.Server;
using EBICO.Server.Handlers;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;
using EBICO.Server.Transactions;
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
        services.TryAddSingleton<IServerKeyStore, InMemoryServerKeyStore>();
        services.TryAddSingleton<IServerBankKeyStore, InMemoryServerBankKeyStore>();
        services.TryAddSingleton<IMasterDataManager, MasterDataManager>();
        services.TryAddSingleton<IEbicsRequestVerifier, NoOpEbicsRequestVerifier>();
        services.TryAddSingleton<IEbicsErrorMapper, EbicsErrorMapper>();
        services.TryAddSingleton<EbicsResponseFactory>();
        services.TryAddSingleton<IEbicsOrderHandlerResolver, EbicsOrderHandlerResolver>();

        // Transaction engine (issue #32): the upload transaction store and the two-phase engine the
        // pipeline routes FUL/BTU initialisations and every transfer-phase request to.
        services.TryAddSingleton<IUploadTransactionStore, InMemoryUploadTransactionStore>();
        services.TryAddSingleton<IUploadTransactionEngine, UploadTransactionEngine>();

        services.TryAddSingleton<IEbicsRequestPipeline, EbicsRequestPipeline>();

        // The H005 HPB handler self-signs the bank's certificates and needs a clock for their validity
        // window (M4 timestamps/nonces will reuse it).
        services.TryAddSingleton(TimeProvider.System);

        // Key-management handlers, one per protocol version (the resolver matches by
        // (Version, OrderType)). AddSingleton (not TryAdd): the resolver consumes the whole
        // IEnumerable<IEbicsOrderHandler>, so several handlers coexist.
        services.AddSingleton<IEbicsOrderHandler, H003IniOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H004IniOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H005IniOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H003HiaOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H004HiaOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H005HiaOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H003HpbOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H004HpbOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H005HpbOrderHandler>();

        // Key-change and suspension handlers (issue #29). HCA/HCS/SPR arrive as signed ebicsRequests;
        // HSA is an ebicsUnsecuredRequest and exists only for H003/H004 (removed in H005).
        services.AddSingleton<IEbicsOrderHandler, H003HcaOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H004HcaOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H005HcaOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H003HcsOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H004HcsOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H005HcsOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H003SprOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H004SprOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H005SprOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H003HsaOrderHandler>();
        services.AddSingleton<IEbicsOrderHandler, H004HsaOrderHandler>();

        return services;
    }
}
