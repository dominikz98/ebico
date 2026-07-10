using AwesomeAssertions;
using EBICO.Core.Domain;
using EBICO.Server.State;
using EBICO.Suite.Services;

namespace EBICO.Tests.Suite;

/// <summary>
/// Tests for the live <see cref="EmulatorStateProvider"/> read-model bridge (issue #53): banks,
/// partners and subscribers come from the server-side <see cref="IEbicsStateStore"/>, while the
/// key catalogue is still served from the sample data.
/// </summary>
public class EmulatorStateProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_Banks_Partners_Subscribers_FromStore()
    {
        var store = new InMemoryEbicsStateStore();
        await store.RegisterBankAsync(new Bank(HostId.Create("HOSTA")), _ct);
        await store.RegisterPartnerAsync(new Partner(HostId.Create("HOSTA"), PartnerId.Create("CUST01")), _ct);
        await store.RegisterSubscriberAsync(
            new Subscriber(HostId.Create("HOSTA"), PartnerId.Create("CUST01"), UserId.Create("USER01")), _ct);

        var sut = new EmulatorStateProvider(store, new SampleEmulatorStateProvider());

        (await sut.GetBanksAsync(_ct)).Should().ContainSingle(b => b.HostId.Value == "HOSTA");
        (await sut.GetPartnersAsync(_ct)).Should().ContainSingle(p => p.PartnerId.Value == "CUST01");
        (await sut.GetSubscribersAsync(_ct)).Should().ContainSingle(s => s.UserId.Value == "USER01");
    }

    [Fact]
    public async Task Reflects_LiveStoreMutations()
    {
        var store = new InMemoryEbicsStateStore();
        var sut = new EmulatorStateProvider(store, new SampleEmulatorStateProvider());

        (await sut.GetBanksAsync(_ct)).Should().BeEmpty();

        await store.RegisterBankAsync(new Bank(HostId.Create("HOSTB")), _ct);

        (await sut.GetBanksAsync(_ct)).Should().ContainSingle(b => b.HostId.Value == "HOSTB");
    }

    [Fact]
    public async Task GetKeysAsync_DelegatesToSampleKeys()
    {
        var samples = new SampleEmulatorStateProvider();
        var sut = new EmulatorStateProvider(new InMemoryEbicsStateStore(), samples);

        var keys = await sut.GetKeysAsync(_ct);

        keys.Should().BeSameAs(await samples.GetKeysAsync(_ct));
    }
}
