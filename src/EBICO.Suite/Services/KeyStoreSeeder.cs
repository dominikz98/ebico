using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Suite.Services;

/// <summary>
/// Seeds the in-process server-side key stores with the deterministic sample key material
/// (<see cref="KeyStoreSeedData"/>) on start-up, so the key/certificate view (issue #55) has real
/// store content to show against the otherwise-empty in-memory stores.
/// </summary>
/// <remarks>
/// Subscriber public keys go into <see cref="IServerKeyStore"/> (as INI/HIA would during onboarding),
/// the bank key pair into <see cref="IServerBankKeyStore"/> (as HPB would). Both store operations are
/// idempotent upserts, so seeding twice is harmless — mirrors <see cref="EmulatorStateSeeder"/>.
/// </remarks>
public static class KeyStoreSeeder
{
    /// <summary>Seeds the sample key material into the key stores resolved from <paramref name="services"/>.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the sample keys have been stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var keyStore = scope.ServiceProvider.GetRequiredService<IServerKeyStore>();
        var bankKeyStore = scope.ServiceProvider.GetRequiredService<IServerBankKeyStore>();

        foreach (var (subscriber, key) in KeyStoreSeedData.SubscriberKeys)
        {
            await keyStore.StoreAsync(subscriber, key, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (host, pair) in KeyStoreSeedData.BankKeys)
        {
            await bankKeyStore.SetAsync(host, pair, cancellationToken).ConfigureAwait(false);
        }
    }
}
