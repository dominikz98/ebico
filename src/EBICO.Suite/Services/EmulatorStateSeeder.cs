using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Suite.Services;

/// <summary>
/// Seeds the in-process emulator store with a small, deterministic set of sample banks, partners
/// and subscribers on start-up, so the read-only views (dashboard, key view) and the master-data
/// management UI (issue #53) have something to show against the otherwise-empty in-memory store.
/// </summary>
/// <remarks>
/// The sample aggregates are taken from <see cref="SampleEmulatorStateProvider"/> and registered
/// through the <see cref="IMasterDataManager"/> in dependency order (banks → partners →
/// subscribers) so the manager's referential-integrity checks are satisfied. Registration is an
/// idempotent upsert, so seeding twice is harmless.
/// </remarks>
public static class EmulatorStateSeeder
{
    /// <summary>Seeds the sample master data into the emulator store resolved from <paramref name="services"/>.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the sample data has been stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IMasterDataManager>();
        var samples = scope.ServiceProvider.GetRequiredService<SampleEmulatorStateProvider>();

        // Order matters: a partner requires its bank, a subscriber its bank and partner.
        foreach (var bank in await samples.GetBanksAsync(cancellationToken).ConfigureAwait(false))
        {
            await manager.SaveBankAsync(bank, cancellationToken).ConfigureAwait(false);
        }

        foreach (var partner in await samples.GetPartnersAsync(cancellationToken).ConfigureAwait(false))
        {
            await manager.SavePartnerAsync(partner, cancellationToken).ConfigureAwait(false);
        }

        foreach (var subscriber in await samples.GetSubscribersAsync(cancellationToken).ConfigureAwait(false))
        {
            await manager.SaveSubscriberAsync(subscriber, cancellationToken).ConfigureAwait(false);
        }
    }
}
