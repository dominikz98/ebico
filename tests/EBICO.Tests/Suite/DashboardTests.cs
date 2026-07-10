using AwesomeAssertions;
using Bunit;
using EBICO.Core.Domain;
using EBICO.Suite.Components.Pages;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit test for the dashboard (<see cref="Home"/>) verifying that it binds against
/// <see cref="IEmulatorStateProvider"/> and shows the emulator state counts (issue #52).
/// </summary>
public class DashboardTests
{
    private sealed class FakeStateProvider(int banks, int partners, int subscribers) : IEmulatorStateProvider
    {
        public Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Bank>>(
                [.. Enumerable.Range(0, banks).Select(i => new Bank(HostId.Create($"HOST{i}")))]);

        public Task<IReadOnlyList<Partner>> GetPartnersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Partner>>(
                [.. Enumerable.Range(0, partners).Select(i => new Partner(HostId.Create($"HOST{i}"), PartnerId.Create($"PART{i}")))]);

        public Task<IReadOnlyList<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Subscriber>>(
                [.. Enumerable.Range(0, subscribers).Select(i =>
                    new Subscriber(HostId.Create("HOST0"), PartnerId.Create("PART0"), UserId.Create($"USER{i}")))]);

        public Task<IReadOnlyList<KeyView>> GetKeysAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KeyView>>([]);
    }

    [Fact]
    public void Dashboard_ShowsCountsFromStateProvider()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddScoped<IEmulatorStateProvider>(_ => new FakeStateProvider(banks: 3, partners: 1, subscribers: 2));

        var cut = ctx.Render<Home>();

        var values = cut.FindAll(".ebico-card-value").Select(e => e.TextContent.Trim()).ToArray();
        values.Should().BeEquivalentTo(["3", "1", "2"]);
    }
}
