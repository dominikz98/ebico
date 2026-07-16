using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Administrative;
using EBICO.Core.Domain;
using EBICO.Server.State;

namespace EBICO.Tests.Server;

/// <summary>
/// Unit tests for the in-memory open-VEU store (issue #42): order-id assignment, retrieval/listing and the
/// sign/cancel state machine (duplicate signer, completion, removal).
/// </summary>
public class OpenVeuStoreTests
{
    private static readonly HostId Host = HostId.Create("EBICOHOST");
    private static readonly PartnerId Partner = PartnerId.Create("PARTNER01");

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAsync_AssignsOrderId_MatchingThePattern()
    {
        var store = new InMemoryOpenVeuStore();

        var first = await store.AddAsync(NewOrder(), _ct);
        var second = await store.AddAsync(NewOrder(), _ct);

        first.OrderId.Should().MatchRegex("^[A-Z][A-Z0-9]{3}$");
        second.OrderId.Should().MatchRegex("^[A-Z][A-Z0-9]{3}$");
        second.OrderId.Should().NotBe(first.OrderId);

        (await store.TryGetAsync(Host, Partner, first.OrderId, _ct)).Should().BeSameAs(first);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyThePartnersOrders()
    {
        var store = new InMemoryOpenVeuStore();
        await store.AddAsync(NewOrder(), _ct);
        await store.AddAsync(NewOrder(), _ct);
        await store.AddAsync(NewOrder(PartnerId.Create("OTHER")), _ct);

        var list = await store.ListAsync(Host, Partner, _ct);

        list.Should().HaveCount(2);
        list.Should().OnlyContain(o => o.PartnerId == Partner);
    }

    [Fact]
    public async Task TrySignAsync_AddsSignature_AndReachesCompletion()
    {
        var store = new InMemoryOpenVeuStore();
        var order = await store.AddAsync(NewOrder(), _ct);

        var first = await store.TrySignAsync(Host, Partner, order.OrderId, Signer("USER02", SignatureClass.A), _ct);
        first.Status.Should().Be(VeuSignStatus.Signed);
        first.Order!.NumSigDone.Should().Be(1);
        first.Order.IsFullySigned.Should().BeFalse();

        var second = await store.TrySignAsync(Host, Partner, order.OrderId, Signer("USER03", SignatureClass.B), _ct);
        second.Status.Should().Be(VeuSignStatus.Signed);
        second.Order!.IsFullySigned.Should().BeTrue();
    }

    [Fact]
    public async Task TrySignAsync_RejectsDuplicateSigner()
    {
        var store = new InMemoryOpenVeuStore();
        var order = await store.AddAsync(NewOrder(), _ct);

        await store.TrySignAsync(Host, Partner, order.OrderId, Signer("USER02", SignatureClass.A), _ct);
        var again = await store.TrySignAsync(Host, Partner, order.OrderId, Signer("USER02", SignatureClass.A), _ct);

        again.Status.Should().Be(VeuSignStatus.DuplicateSigner);
        order.NumSigDone.Should().Be(1);
    }

    [Fact]
    public async Task TrySignAsync_UnknownOrder_ReturnsNotFound()
    {
        var store = new InMemoryOpenVeuStore();

        var outcome = await store.TrySignAsync(Host, Partner, "ZZZZ", Signer("USER02", SignatureClass.A), _ct);

        outcome.Status.Should().Be(VeuSignStatus.NotFound);
        outcome.Order.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_RemovesTheOrder()
    {
        var store = new InMemoryOpenVeuStore();
        var order = await store.AddAsync(NewOrder(), _ct);

        (await store.RemoveAsync(Host, Partner, order.OrderId, _ct)).Should().BeTrue();
        (await store.TryGetAsync(Host, Partner, order.OrderId, _ct)).Should().BeNull();
        (await store.RemoveAsync(Host, Partner, order.OrderId, _ct)).Should().BeFalse();
    }

    private static OpenVeuOrder NewOrder(PartnerId? partner = null)
        => new(
            Host,
            partner ?? Partner,
            EbicsVersion.H005,
            "CCT",
            [0x01, 0x02, 0x03],
            new VeuSignerView((partner ?? Partner).Value, "USER01", "Alice", DateTimeOffset.UnixEpoch, Permission: null),
            numSigRequired: 2,
            createdAt: DateTimeOffset.UnixEpoch);

    private static VeuSignerView Signer(string userId, SignatureClass permission)
        => new(Partner.Value, userId, userId, DateTimeOffset.UnixEpoch, permission);
}
