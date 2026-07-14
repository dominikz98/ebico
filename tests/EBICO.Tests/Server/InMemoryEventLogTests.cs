using AwesomeAssertions;
using EBICO.Core.Domain;
using EBICO.Server;
using EBICO.Server.State;
using EBICO.Tests.Connector;
using Microsoft.Extensions.Options;

namespace EBICO.Tests.Server;

/// <summary>
/// Unit tests for the in-memory append-only event log (issue #69): append semantics (monotonic
/// sequence, clock-stamped timestamp, ring-buffer bound, thread-safety) and the query filters
/// (customer/type/visibility/time range/limit), including the visibility rule the HAC projection relies
/// on. Exercised directly against <see cref="InMemoryEventLog"/>, independently of the pipeline.
/// </summary>
public class InMemoryEventLogTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static InMemoryEventLog NewLog(MutableTimeProvider clock, int maxEntries = 0)
        => new(clock, Options.Create(new EbicoServerOptions { MaxEventLogEntries = maxEntries }));

    private static EbicsEvent Event(
        EbicsEventType type = EbicsEventType.RequestReceived,
        string? host = null,
        string? partner = null,
        string? user = null,
        EbicsEventVisibility visibility = EbicsEventVisibility.CustomerVisible,
        string message = "test")
        => new()
        {
            Type = type,
            Visibility = visibility,
            HostId = host is null ? null : HostId.Create(host),
            PartnerId = partner is null ? null : PartnerId.Create(partner),
            UserId = user is null ? null : UserId.Create(user),
            Message = message,
        };

    // --- Append ----------------------------------------------------------------------------

    [Fact]
    public async Task AppendAsync_AssignsMonotonicSequence_AndStampsTimestampFromClock()
    {
        var clock = new MutableTimeProvider(Start);
        var log = NewLog(clock);

        await log.AppendAsync(Event(message: "first"), _ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        await log.AppendAsync(Event(message: "second"), _ct);

        var all = await log.QueryAsync(new EbicsEventQuery(), _ct);

        all.Should().HaveCount(2);
        all[0].Sequence.Should().Be(1);
        all[0].Timestamp.Should().Be(Start);
        all[1].Sequence.Should().Be(2);
        all[1].Timestamp.Should().Be(Start + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task AppendAsync_OverwritesCallerSuppliedSequenceAndTimestamp()
    {
        var clock = new MutableTimeProvider(Start);
        var log = NewLog(clock);

        // A caller that sets Sequence/Timestamp is ignored — the log owns them.
        await log.AppendAsync(Event() with { Sequence = 999, Timestamp = DateTimeOffset.UnixEpoch }, _ct);

        var only = (await log.QueryAsync(new EbicsEventQuery(), _ct)).Single();
        only.Sequence.Should().Be(1);
        only.Timestamp.Should().Be(Start);
    }

    [Fact]
    public async Task AppendAsync_NullEvent_Throws()
    {
        var log = NewLog(new MutableTimeProvider(Start));

        var act = async () => await log.AppendAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // --- Query: basics ---------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_EmptyLog_ReturnsEmpty()
    {
        var log = NewLog(new MutableTimeProvider(Start));

        (await log.QueryAsync(new EbicsEventQuery(), _ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_NullQuery_Throws()
    {
        var log = NewLog(new MutableTimeProvider(Start));

        var act = async () => await log.QueryAsync(null!, _ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task QueryAsync_ReturnsEventsInAscendingSequenceOrder()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        for (var i = 0; i < 5; i++)
        {
            await log.AppendAsync(Event(message: $"e{i}"), _ct);
        }

        var all = await log.QueryAsync(new EbicsEventQuery(), _ct);

        all.Select(e => e.Sequence).Should().Equal([1, 2, 3, 4, 5]);
    }

    // --- Query: filters --------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_FiltersByPartnerId()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        await log.AppendAsync(Event(partner: "PARTNER01"), _ct);
        await log.AppendAsync(Event(partner: "PARTNER02"), _ct);
        await log.AppendAsync(Event(partner: "PARTNER01"), _ct);

        var result = await log.QueryAsync(new EbicsEventQuery { PartnerId = PartnerId.Create("PARTNER01") }, _ct);

        result.Should().HaveCount(2).And.OnlyContain(e => e.PartnerId == PartnerId.Create("PARTNER01"));
    }

    [Fact]
    public async Task QueryAsync_FiltersByUserId()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        await log.AppendAsync(Event(user: "USER01"), _ct);
        await log.AppendAsync(Event(user: "USER02"), _ct);

        var result = await log.QueryAsync(new EbicsEventQuery { UserId = UserId.Create("USER02") }, _ct);

        result.Should().ContainSingle().Which.UserId.Should().Be(UserId.Create("USER02"));
    }

    [Fact]
    public async Task QueryAsync_FiltersByHostId()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        await log.AppendAsync(Event(host: "HOSTA"), _ct);
        await log.AppendAsync(Event(host: "HOSTB"), _ct);

        var result = await log.QueryAsync(new EbicsEventQuery { HostId = HostId.Create("HOSTA") }, _ct);

        result.Should().ContainSingle().Which.HostId.Should().Be(HostId.Create("HOSTA"));
    }

    [Fact]
    public async Task QueryAsync_FiltersByType()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        await log.AppendAsync(Event(EbicsEventType.RequestReceived), _ct);
        await log.AppendAsync(Event(EbicsEventType.UploadCompleted), _ct);
        await log.AppendAsync(Event(EbicsEventType.UploadCompleted), _ct);

        var result = await log.QueryAsync(new EbicsEventQuery { Type = EbicsEventType.UploadCompleted }, _ct);

        result.Should().HaveCount(2).And.OnlyContain(e => e.Type == EbicsEventType.UploadCompleted);
    }

    [Fact]
    public async Task QueryAsync_NoMatch_ReturnsEmpty()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        await log.AppendAsync(Event(partner: "PARTNER01"), _ct);

        var result = await log.QueryAsync(new EbicsEventQuery { PartnerId = PartnerId.Create("NOBODY") }, _ct);

        result.Should().BeEmpty();
    }

    // --- Query: visibility rule (HAC projection) -------------------------------------------

    [Fact]
    public async Task QueryAsync_VisibilityCustomerVisible_ExcludesInternalEvents()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        await log.AppendAsync(Event(visibility: EbicsEventVisibility.CustomerVisible, message: "visible"), _ct);
        await log.AppendAsync(Event(visibility: EbicsEventVisibility.Internal, message: "internal"), _ct);
        await log.AppendAsync(Event(visibility: EbicsEventVisibility.CustomerVisible, message: "visible2"), _ct);

        // HAC reads only customer-visible events.
        var customer = await log.QueryAsync(new EbicsEventQuery { Visibility = EbicsEventVisibility.CustomerVisible }, _ct);
        customer.Should().HaveCount(2).And.OnlyContain(e => e.Visibility == EbicsEventVisibility.CustomerVisible);

        // The Suite inspector reads everything (no visibility filter).
        var all = await log.QueryAsync(new EbicsEventQuery(), _ct);
        all.Should().HaveCount(3);
    }

    // --- Query: time range -----------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_TimeRange_FromInclusive_ToExclusive()
    {
        var clock = new MutableTimeProvider(Start);
        var log = NewLog(clock);

        await log.AppendAsync(Event(message: "t0"), _ct);          // Start
        clock.Advance(TimeSpan.FromMinutes(1));
        await log.AppendAsync(Event(message: "t1"), _ct);          // Start + 1
        clock.Advance(TimeSpan.FromMinutes(1));
        await log.AppendAsync(Event(message: "t2"), _ct);          // Start + 2
        clock.Advance(TimeSpan.FromMinutes(1));
        await log.AppendAsync(Event(message: "t3"), _ct);          // Start + 3

        var result = await log.QueryAsync(
            new EbicsEventQuery
            {
                From = Start + TimeSpan.FromMinutes(1),
                To = Start + TimeSpan.FromMinutes(3),
            },
            _ct);

        // From is inclusive (t1 kept), To is exclusive (t3 dropped); t0 is before the window.
        result.Select(e => e.Message).Should().Equal(["t1", "t2"]);
    }

    // --- Query: limit ----------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_Limit_ReturnsEarliestMatches()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        for (var i = 0; i < 5; i++)
        {
            await log.AppendAsync(Event(message: $"e{i}"), _ct);
        }

        var result = await log.QueryAsync(new EbicsEventQuery { Limit = 2 }, _ct);

        result.Select(e => e.Sequence).Should().Equal([1, 2]);
    }

    [Fact]
    public async Task QueryAsync_LimitGreaterThanCount_ReturnsAll()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        await log.AppendAsync(Event(), _ct);
        await log.AppendAsync(Event(), _ct);

        var result = await log.QueryAsync(new EbicsEventQuery { Limit = 99 }, _ct);

        result.Should().HaveCount(2);
    }

    // --- Bounds & concurrency --------------------------------------------------------------

    [Fact]
    public async Task AppendAsync_BeyondMaxEntries_DropsOldest_ButKeepsSequenceGrowing()
    {
        var log = NewLog(new MutableTimeProvider(Start), maxEntries: 3);
        for (var i = 0; i < 5; i++)
        {
            await log.AppendAsync(Event(message: $"e{i}"), _ct);
        }

        var all = await log.QueryAsync(new EbicsEventQuery(), _ct);

        // Only the last three survive; their sequence numbers keep the original (3,4,5) ordering.
        all.Should().HaveCount(3);
        all.Select(e => e.Sequence).Should().Equal([3, 4, 5]);
        all.Select(e => e.Message).Should().Equal(["e2", "e3", "e4"]);
    }

    [Fact]
    public async Task AppendAsync_Concurrent_AssignsUniqueSequences()
    {
        var log = NewLog(new MutableTimeProvider(Start));
        const int count = 500;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            _ct,
            async (i, token) => await log.AppendAsync(Event(message: $"e{i}"), token));

        var all = await log.QueryAsync(new EbicsEventQuery(), _ct);

        all.Should().HaveCount(count);
        all.Select(e => e.Sequence).Distinct().Should().HaveCount(count);
        all.Select(e => e.Sequence).Should().BeInAscendingOrder();
    }
}
