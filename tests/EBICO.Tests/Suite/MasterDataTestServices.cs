using Bunit;
using EBICO.Server.State;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// Wires a <see cref="BunitContext"/> with the real server-side master-data services (in-memory
/// store + <see cref="MasterDataManager"/>) and the Suite read-model bridge
/// (<see cref="EmulatorStateProvider"/>), so the management-component tests (issue #53) exercise
/// the actual #30 logic rather than a fake. Returns the manager for per-test seeding.
/// </summary>
internal static class MasterDataTestServices
{
    public static IMasterDataManager Configure(BunitContext ctx)
    {
        ctx.Services.AddSingleton<IEbicsStateStore, InMemoryEbicsStateStore>();
        ctx.Services.AddSingleton<IMasterDataManager, MasterDataManager>();
        ctx.Services.AddSingleton<SampleEmulatorStateProvider>();
        ctx.Services.AddScoped<IEmulatorStateProvider, EmulatorStateProvider>();
        return ctx.Services.GetRequiredService<IMasterDataManager>();
    }
}
