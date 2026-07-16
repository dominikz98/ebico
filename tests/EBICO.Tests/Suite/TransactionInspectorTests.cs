using AwesomeAssertions;
using Bunit;
using EBICO.Server.State;
using EBICO.Server.Transactions;
using EBICO.Suite.Components.Transaktionen;
using EBICO.Suite.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Suite;

/// <summary>
/// bUnit tests for the transaction inspector island (<see cref="TransactionInspector"/>, issue #54): the
/// transaction list with status badges, the detail view with raw XML / order data / events, the global
/// event log with a live severity filter, and the "jump event → transaction" navigation. Rendered against
/// a fake <see cref="ITransactionInspectorProvider"/>.
/// </summary>
public class TransactionInspectorTests
{
    [Fact]
    public void List_RendersRows_WithStatusBadges()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddScoped<ITransactionInspectorProvider>(_ => new FakeInspector());

        var cut = ctx.Render<TransactionInspector>();

        cut.Find("#tx-row-AA").Should().NotBeNull();
        cut.Find("#tx-row-BB").Should().NotBeNull();
        var badges = cut.FindAll("#tx-list .badge").Select(e => e.TextContent.Trim()).ToArray();
        badges.Should().Contain("Completed").And.Contain("Failed");
    }

    [Fact]
    public void Details_OpensDetail_WithRawXml()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddScoped<ITransactionInspectorProvider>(_ => new FakeInspector());

        var cut = ctx.Render<TransactionInspector>();
        cut.Find("#tx-row-AA button").Click();

        var detail = cut.Find("#tx-detail");
        detail.TextContent.Should().Contain("DEMO-REQ");
        detail.TextContent.Should().Contain("DEMO-RESP");
    }

    [Fact]
    public void Details_OrderDataTab_ShowsDecryptedText()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddScoped<ITransactionInspectorProvider>(_ => new FakeInspector());

        var cut = ctx.Render<TransactionInspector>();
        cut.Find("#tx-row-AA button").Click();
        cut.Find("#tab-orderdata").Click();

        cut.Find("#tab-panel-orderdata").TextContent.Should().Contain("pain demo");
    }

    [Fact]
    public void EventJump_SelectsTransaction()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddScoped<ITransactionInspectorProvider>(_ => new FakeInspector());

        var cut = ctx.Render<TransactionInspector>();
        // Event seq 1 carries transaction AA.
        cut.Find("#event-jump-1").Click();

        cut.Find("#tx-detail").TextContent.Should().Contain("DEMO-REQ");
    }

    [Fact]
    public void SeverityFilter_IsAppliedLive()
    {
        using var ctx = new BunitContext();
        var fake = new FakeInspector();
        ctx.Services.AddScoped<ITransactionInspectorProvider>(_ => fake);

        var cut = ctx.Render<TransactionInspector>();
        cut.Find("#filter-severity").Change("Warning");

        fake.LastFilter!.Severity.Should().Be(EbicsEventSeverity.Warning);
        // Only the single Warning event survives the filter.
        var rows = cut.FindAll("#event-log tbody tr");
        rows.Should().ContainSingle();
        cut.Find("#event-log").TextContent.Should().Contain("PARTNER02");
    }

    private sealed class FakeInspector : ITransactionInspectorProvider
    {
        public EventLogFilter? LastFilter { get; private set; }

        private readonly IReadOnlyList<EventView> _events =
        [
            new() { Sequence = 1, Type = EbicsEventType.UploadStarted, Severity = EbicsEventSeverity.Info, PartnerLabel = "PARTNER01", UserLabel = "USER01", OrderType = "BTU", TransactionIdHex = "AA", Message = "Upload started" },
            new() { Sequence = 2, Type = EbicsEventType.RequestReceived, Severity = EbicsEventSeverity.Warning, PartnerLabel = "PARTNER02", UserLabel = "USER09", OrderType = "BTD", TransactionIdHex = "BB", Message = "rejected" },
        ];

        public Task<IReadOnlyList<TransactionSummary>> GetTransactionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TransactionSummary>>(
            [
                new()
                {
                    TransactionIdHex = "AA", Kind = TransactionKind.Upload, OrderType = "BTU",
                    PartnerLabel = "PARTNER01", UserLabel = "USER01", NumSegments = 2,
                    Status = TransactionStatus.Completed, LastReturnCode = "000000 EBICS_OK",
                    CreatedAt = DateTimeOffset.UnixEpoch, LastActivityAt = DateTimeOffset.UnixEpoch,
                    EventCount = 2, HasCapture = true, IsResident = true,
                },
                new()
                {
                    TransactionIdHex = "BB", Kind = TransactionKind.Download, OrderType = "BTD",
                    PartnerLabel = "PARTNER02", UserLabel = "USER09",
                    Status = TransactionStatus.Failed, LastReturnCode = "090004 EBICS_INVALID_ORDER_DATA_FORMAT",
                    CreatedAt = DateTimeOffset.UnixEpoch, LastActivityAt = DateTimeOffset.UnixEpoch,
                    EventCount = 1, HasCapture = false, IsResident = false,
                },
            ]);

        public Task<TransactionDetail?> GetTransactionAsync(string transactionIdHex, CancellationToken cancellationToken = default)
        {
            if (transactionIdHex != "AA")
            {
                return Task.FromResult<TransactionDetail?>(null);
            }

            var detail = new TransactionDetail
            {
                Summary = new TransactionSummary
                {
                    TransactionIdHex = "AA", Kind = TransactionKind.Upload, OrderType = "BTU",
                    HostLabel = "EBICOHOST", PartnerLabel = "PARTNER01", UserLabel = "USER01", NumSegments = 2,
                    Status = TransactionStatus.Completed, LastReturnCode = "000000 EBICS_OK",
                    CreatedAt = DateTimeOffset.UnixEpoch, LastActivityAt = DateTimeOffset.UnixEpoch,
                    EventCount = 2, HasCapture = true, IsResident = true,
                },
                Events =
                [
                    new() { Sequence = 1, Type = EbicsEventType.UploadStarted, Severity = EbicsEventSeverity.Info, ReturnCode = "000000 EBICS_OK", Message = "Upload started" },
                ],
                Messages =
                [
                    new()
                    {
                        Phase = EbicsTransactionPhase.Initialisation,
                        RequestXml = "<ebicsRequest>DEMO-REQ</ebicsRequest>",
                        ResponseXml = "<ebicsResponse>DEMO-RESP</ebicsResponse>",
                    },
                ],
                OrderData = new OrderDataView { ByteLength = 9, IsText = true, Text = "pain demo", Hex = "7061696E2064656D6F" },
            };

            return Task.FromResult<TransactionDetail?>(detail);
        }

        public Task<IReadOnlyList<EventView>> GetEventsAsync(EventLogFilter filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            IEnumerable<EventView> matched = _events;
            if (filter.Partner is not null)
            {
                matched = matched.Where(e => e.PartnerLabel == filter.Partner);
            }

            if (filter.Severity is { } severity)
            {
                matched = matched.Where(e => e.Severity == severity);
            }

            return Task.FromResult<IReadOnlyList<EventView>>(matched.ToList());
        }

        public Task<IReadOnlyList<string>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(["PARTNER01", "PARTNER02"]);
    }
}
