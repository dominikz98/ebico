using AwesomeAssertions;
using EBICO.Core.Domain;
using EBICO.Server.State;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// Tests for <see cref="EmulatorStateSeeder"/> (issue #53): the sample master data is registered
/// through the <see cref="IMasterDataManager"/> in dependency order and the seeding is idempotent.
/// </summary>
public class EmulatorStateSeederTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEbicsStateStore, InMemoryEbicsStateStore>();
        services.AddSingleton<IMasterDataManager, MasterDataManager>();
        services.AddSingleton<SampleEmulatorStateProvider>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedAsync_PopulatesManagerWithSampleData()
    {
        await using var provider = BuildProvider();

        await EmulatorStateSeeder.SeedAsync(provider, _ct);

        var manager = provider.GetRequiredService<IMasterDataManager>();
        (await manager.GetBanksAsync(_ct)).Should().HaveCount(2);
        (await manager.GetPartnersAsync(HostId.Create("EBICOHOST"), _ct)).Should().HaveCount(2);
        (await manager.GetSubscribersAsync(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), _ct))
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await using var provider = BuildProvider();

        await EmulatorStateSeeder.SeedAsync(provider, _ct);
        await EmulatorStateSeeder.SeedAsync(provider, _ct);

        var manager = provider.GetRequiredService<IMasterDataManager>();
        (await manager.GetBanksAsync(_ct)).Should().HaveCount(2);
        (await manager.GetSubscribersAsync(HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), _ct))
            .Should().HaveCount(2);
    }
}
