using AwesomeAssertions;
using EBICO.Server;
using EBICO.Server.State;
using EBICO.Server.Transactions;
using EBICO.Tests.Connector;
using Microsoft.Extensions.Options;

namespace EBICO.Tests.Server;

/// <summary>
/// Unit tests for the in-memory raw-message capture store (issue #54): append semantics (monotonic
/// sequence, clock-stamped timestamp, ring-buffer bound, XML truncation) and the keyed per-transaction
/// lookup. Exercised directly against <see cref="InMemoryMessageCaptureStore"/>, independently of the pipeline.
/// </summary>
public class InMemoryMessageCaptureStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static InMemoryMessageCaptureStore NewStore(MutableTimeProvider clock, int maxEntries = 0, int maxBytes = 0)
        => new(clock, Options.Create(new EbicoServerOptions { MaxMessageCaptureEntries = maxEntries, MaxCapturedMessageBytes = maxBytes }));

    private static CapturedMessage Message(
        string transactionIdHex,
        EbicsTransactionPhase phase = EbicsTransactionPhase.Initialisation,
        string request = "<request/>",
        string response = "<response/>")
        => new()
        {
            TransactionIdHex = transactionIdHex,
            Phase = phase,
            RequestXml = request,
            ResponseXml = response,
        };

    // --- Append ----------------------------------------------------------------------------

    [Fact]
    public async Task AppendAsync_AssignsMonotonicSequence_AndStampsTimestampFromClock()
    {
        var clock = new MutableTimeProvider(Start);
        var store = NewStore(clock);

        await store.AppendAsync(Message("AA"), _ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        await store.AppendAsync(Message("AA", EbicsTransactionPhase.Transfer), _ct);

        var messages = await store.GetAsync("AA", _ct);

        messages.Should().HaveCount(2);
        messages[0].Sequence.Should().Be(1);
        messages[0].Timestamp.Should().Be(Start);
        messages[1].Sequence.Should().Be(2);
        messages[1].Timestamp.Should().Be(Start + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task AppendAsync_OverwritesCallerSuppliedSequenceAndTimestamp()
    {
        var store = NewStore(new MutableTimeProvider(Start));

        await store.AppendAsync(Message("AA") with { Sequence = 999, Timestamp = DateTimeOffset.UnixEpoch }, _ct);

        var only = (await store.GetAsync("AA", _ct)).Single();
        only.Sequence.Should().Be(1);
        only.Timestamp.Should().Be(Start);
    }

    [Fact]
    public async Task AppendAsync_NullMessage_Throws()
    {
        var store = NewStore(new MutableTimeProvider(Start));

        var act = async () => await store.AppendAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // --- Get -------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_NullId_Throws()
    {
        var store = NewStore(new MutableTimeProvider(Start));

        var act = async () => await store.GetAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_UnknownTransaction_ReturnsEmpty()
    {
        var store = NewStore(new MutableTimeProvider(Start));
        await store.AppendAsync(Message("AA"), _ct);

        (await store.GetAsync("BB", _ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyMatchingTransaction_InAscendingSequenceOrder()
    {
        var store = NewStore(new MutableTimeProvider(Start));
        await store.AppendAsync(Message("AA", EbicsTransactionPhase.Initialisation), _ct);
        await store.AppendAsync(Message("BB", EbicsTransactionPhase.Initialisation), _ct);
        await store.AppendAsync(Message("AA", EbicsTransactionPhase.Transfer), _ct);

        var forAa = await store.GetAsync("AA", _ct);

        forAa.Should().HaveCount(2);
        forAa.Select(m => m.Sequence).Should().Equal([1, 3]);
        forAa.Select(m => m.Phase).Should().Equal([EbicsTransactionPhase.Initialisation, EbicsTransactionPhase.Transfer]);
    }

    // --- Bounds & truncation ---------------------------------------------------------------

    [Fact]
    public async Task AppendAsync_BeyondMaxEntries_DropsOldest()
    {
        var store = NewStore(new MutableTimeProvider(Start), maxEntries: 2);
        await store.AppendAsync(Message("AA"), _ct);
        await store.AppendAsync(Message("AA"), _ct);
        await store.AppendAsync(Message("AA"), _ct);

        var messages = await store.GetAsync("AA", _ct);

        // Ring buffer keeps the last two; sequence numbers keep growing.
        messages.Should().HaveCount(2);
        messages.Select(m => m.Sequence).Should().Equal([2, 3]);
    }

    [Fact]
    public async Task AppendAsync_OversizedXml_IsTruncated_AndFlagged()
    {
        var store = NewStore(new MutableTimeProvider(Start), maxBytes: 5);

        await store.AppendAsync(Message("AA", request: "0123456789", response: "ok"), _ct);

        var only = (await store.GetAsync("AA", _ct)).Single();
        only.RequestXml.Should().Be("01234");
        only.RequestTruncated.Should().BeTrue();
        only.ResponseXml.Should().Be("ok");
        only.ResponseTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_WithinLimit_IsNotTruncated()
    {
        var store = NewStore(new MutableTimeProvider(Start), maxBytes: 1024);

        await store.AppendAsync(Message("AA", request: "<request/>"), _ct);

        var only = (await store.GetAsync("AA", _ct)).Single();
        only.RequestXml.Should().Be("<request/>");
        only.RequestTruncated.Should().BeFalse();
    }
}
