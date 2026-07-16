using EBICO.Server;
using EBICO.Server.Handlers;
using EBICO.Server.Orders;
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
        // Shared append-only event/protocol log (issue #69): the source HAC (M5) and the Suite inspector
        // (M7) read from. In-memory default, pluggable via TryAddSingleton (SQLite is a follow-up, ADR-0015).
        services.TryAddSingleton<IEventLog, InMemoryEventLog>();
        services.TryAddSingleton<IServerBankKeyStore, InMemoryServerBankKeyStore>();
        services.TryAddSingleton<IMasterDataManager, MasterDataManager>();
        services.TryAddSingleton<IEbicsRequestVerifier, NoOpEbicsRequestVerifier>();
        services.TryAddSingleton<IEbicsErrorMapper, EbicsErrorMapper>();
        services.TryAddSingleton<EbicsResponseFactory>();
        services.TryAddSingleton<IEbicsOrderHandlerResolver, EbicsOrderHandlerResolver>();

        // Order processing (issues #39/#42): the order-type-specific post-processing the upload engine
        // invokes once the order data is decoded. The engine consumes the whole IEnumerable and uses the
        // first processor whose CanProcess matches (AddSingleton, not TryAdd, so several coexist — a caller
        // can add its own before AddEbicoServer). Defaults: SEPA payments (CCT/CDD/CDB/CIP) — validate the
        // pain payload and either file the pain.002 status report or park the order for distributed signing
        // — and the VEU signature/cancellation orders (HVE/HVS) that sign/release/cancel a parked order.
        services.AddSingleton<IUploadOrderProcessor, SepaPaymentUploadProcessor>();
        services.AddSingleton<IUploadOrderProcessor, VeuSignatureUploadProcessor>();

        // Open-VEU store (issue #42): the long-lived, partner-scoped store of orders awaiting distributed
        // signatures (parked on upload, mutated by HVE/HVS, projected by HVU/HVZ/HVD/HVT). In-memory
        // default, pluggable via TryAddSingleton.
        services.TryAddSingleton<IOpenVeuStore, InMemoryOpenVeuStore>();

        // Transaction engine (issue #32): the upload transaction store and the two-phase engine the
        // pipeline routes FUL/BTU initialisations and every transfer-phase request to.
        services.TryAddSingleton<IUploadTransactionStore, InMemoryUploadTransactionStore>();
        services.TryAddSingleton<IUploadTransactionEngine, UploadTransactionEngine>();

        // Download transaction engine (issue #33): the download transaction store, the order-data
        // provider (seedable via the admin API) and the three-phase engine the pipeline routes FDL/BTD
        // initialisations, the receipt phase and matching transfer-phase requests to.
        services.TryAddSingleton<IDownloadTransactionStore, InMemoryDownloadTransactionStore>();
        services.TryAddSingleton<IDownloadDataProvider, InMemoryDownloadDataProvider>();

        // Download order processing (issues #40/#41): on-demand generation of order data when no payload is
        // pre-seeded for the resolved order type. The engine consumes the whole IEnumerable and uses the
        // first processor whose CanProcess matches (AddSingleton, not TryAdd, so several coexist — a caller
        // can add its own before AddEbicoServer). Defaults: synthetic MT940/MT942/camt.05x statements
        // (STA/VMK/C53/C52/C54), subscriber/parameter data (HTD/HKD/HAA/HPD) and the customer protocol
        // (HAC/PTK) projected from the event log.
        services.AddSingleton<IDownloadOrderProcessor, StatementDownloadProcessor>();
        services.AddSingleton<IDownloadOrderProcessor, SubscriberInfoDownloadProcessor>();
        services.AddSingleton<IDownloadOrderProcessor, CustomerProtocolDownloadProcessor>();
        // Distributed electronic signature overview/detail (issue #42): HVU/HVZ/HVD/HVT projected from the
        // open-VEU store.
        services.AddSingleton<IDownloadOrderProcessor, VeuOverviewDownloadProcessor>();
        services.TryAddSingleton<IDownloadTransactionEngine, DownloadTransactionEngine>();

        // Transaction recovery/timeouts (issue #35): both engines double as transaction evictors. The
        // forwarding registrations resolve the SAME singleton instances (AddSingleton, not TryAdd, so
        // both land in the IEnumerable<ITransactionEvictor> the cleanup service sweeps). The background
        // sweeper bounds memory even for orphaned transactions; lazy expiry on access complements it.
        services.AddSingleton<ITransactionEvictor>(sp => (ITransactionEvictor)sp.GetRequiredService<IUploadTransactionEngine>());
        services.AddSingleton<ITransactionEvictor>(sp => (ITransactionEvictor)sp.GetRequiredService<IDownloadTransactionEngine>());
        services.AddHostedService<TransactionCleanupService>();

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
