using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Server;
using EBICO.Server.State;
using EBICO.Server.Transactions;
using EBICO.Suite.Services;
using EBICO.Tests.Connector;
using Microsoft.Extensions.Options;

namespace EBICO.Tests.Suite;

/// <summary>
/// Unit tests for the <see cref="TransactionInspectorProvider"/> (issue #54): reconstruction of the
/// transaction list from the event log (grouping, status/kind derivation), enrichment from the transaction
/// stores (residency, decrypted order data), the raw-XML captures in the detail, and the global event-log
/// filters (customer/type/severity/time), including the severity filter applied client-side.
/// </summary>
public class TransactionInspectorProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly SubscriberKeyRef Subscriber = new(
        HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER01"));

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private readonly MutableTimeProvider _clock = new(Start);
    private readonly InMemoryEventLog _log;
    private readonly InMemoryUploadTransactionStore _uploads = new();
    private readonly InMemoryDownloadTransactionStore _downloads = new();
    private readonly InMemoryMessageCaptureStore _captures;
    private readonly TransactionInspectorProvider _provider;

    public TransactionInspectorProviderTests()
    {
        _log = new InMemoryEventLog(_clock, Options.Create(new EbicoServerOptions()));
        _captures = new InMemoryMessageCaptureStore(_clock, Options.Create(new EbicoServerOptions()));
        _provider = new TransactionInspectorProvider(_log, _uploads, _downloads, _captures);
    }

    // --- Transaction list ------------------------------------------------------------------

    [Fact]
    public async Task GetTransactionsAsync_GroupsByTransaction_AndDerivesStatus()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU");
        await AppendAsync(EbicsEventType.UploadCompleted, "AA", "BTU");
        await AppendAsync(EbicsEventType.UploadStarted, "BB", "BTU");
        await AppendAsync(EbicsEventType.DownloadStarted, "CC", "BTD");
        await AppendAsync(EbicsEventType.TransactionEvicted, "CC", "BTD", EbicsEventSeverity.Warning);
        await AppendAsync(EbicsEventType.RequestReceived, "DD", "BTU", EbicsEventSeverity.Warning, EbicsReturnCode.InvalidOrderDataFormat);

        var transactions = await _provider.GetTransactionsAsync(_ct);

        transactions.Should().HaveCount(4);
        Status(transactions, "AA").Should().Be(TransactionStatus.Completed);
        Status(transactions, "BB").Should().Be(TransactionStatus.Running);
        Status(transactions, "CC").Should().Be(TransactionStatus.Evicted);
        Status(transactions, "DD").Should().Be(TransactionStatus.Failed);
    }

    [Fact]
    public async Task GetTransactionsAsync_DerivesKindFromEvents()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU");
        await AppendAsync(EbicsEventType.DownloadStarted, "BB", "BTD");

        var transactions = await _provider.GetTransactionsAsync(_ct);

        Summary(transactions, "AA").Kind.Should().Be(TransactionKind.Upload);
        Summary(transactions, "BB").Kind.Should().Be(TransactionKind.Download);
    }

    [Fact]
    public async Task GetTransactionsAsync_OrdersByLastActivityDescending()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU");
        _clock.Advance(TimeSpan.FromMinutes(1));
        await AppendAsync(EbicsEventType.DownloadStarted, "BB", "BTD");

        var transactions = await _provider.GetTransactionsAsync(_ct);

        transactions.Select(t => t.TransactionIdHex).Should().Equal(["BB", "AA"]);
    }

    // --- Detail ----------------------------------------------------------------------------

    [Fact]
    public async Task GetTransactionAsync_ResidentCompletedUpload_ExposesDecryptedOrderDataAsText()
    {
        var id = Id(0x11);
        var hex = Convert.ToHexString(id);
        var payload = Encoding.UTF8.GetBytes("<Document>pain.001 demo</Document>");

        var tx = new UploadTransaction(id, EbicsVersion.H005, Subscriber, "BTU", numSegments: 1, transactionKey: new byte[16], signatureData: null, createdAt: Start);
        tx.Complete(payload);
        _uploads.Create(tx);
        await AppendAsync(EbicsEventType.UploadStarted, hex, "BTU");
        await AppendAsync(EbicsEventType.UploadCompleted, hex, "BTU");

        var detail = await _provider.GetTransactionAsync(hex, _ct);

        detail.Should().NotBeNull();
        detail!.Summary.IsResident.Should().BeTrue();
        detail.OrderData.Should().NotBeNull();
        detail.OrderData!.IsText.Should().BeTrue();
        detail.OrderData.ByteLength.Should().Be(payload.Length);
        detail.OrderData.Text.Should().Contain("pain.001 demo");
    }

    [Fact]
    public async Task GetTransactionAsync_NonResident_ReturnsDetailWithoutOrderData()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU");
        await AppendAsync(EbicsEventType.UploadCompleted, "AA", "BTU");

        var detail = await _provider.GetTransactionAsync("AA", _ct);

        detail.Should().NotBeNull();
        detail!.Summary.IsResident.Should().BeFalse();
        detail.OrderData.Should().BeNull();
        detail.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTransactionAsync_Unknown_ReturnsNull()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU");

        (await _provider.GetTransactionAsync("ZZ", _ct)).Should().BeNull();
    }

    [Fact]
    public async Task GetTransactionAsync_IncludesCaptures()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU");
        await _captures.AppendAsync(
            new CapturedMessage
            {
                TransactionIdHex = "AA",
                Phase = EbicsTransactionPhase.Initialisation,
                RequestXml = "<ebicsRequest/>",
                ResponseXml = "<ebicsResponse/>",
            },
            _ct);

        var detail = await _provider.GetTransactionAsync("AA", _ct);

        detail!.Messages.Should().ContainSingle();
        detail.Messages[0].Phase.Should().Be(EbicsTransactionPhase.Initialisation);
        detail.Summary.HasCapture.Should().BeTrue();
    }

    // --- Global event log ------------------------------------------------------------------

    [Fact]
    public async Task GetEventsAsync_FiltersBySeverity_ClientSide()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU", EbicsEventSeverity.Info);
        await AppendAsync(EbicsEventType.RequestReceived, "BB", "BTU", EbicsEventSeverity.Warning);
        await AppendAsync(EbicsEventType.RequestReceived, "CC", "BTU", EbicsEventSeverity.Error);

        var warnings = await _provider.GetEventsAsync(new EventLogFilter { Severity = EbicsEventSeverity.Warning }, _ct);

        warnings.Should().ContainSingle().Which.Severity.Should().Be(EbicsEventSeverity.Warning);
    }

    [Fact]
    public async Task GetEventsAsync_FiltersByPartner()
    {
        await _log.AppendAsync(Event(EbicsEventType.UploadStarted, "AA", "BTU", partner: "PARTNER01"), _ct);
        await _log.AppendAsync(Event(EbicsEventType.UploadStarted, "BB", "BTU", partner: "PARTNER02"), _ct);

        var result = await _provider.GetEventsAsync(new EventLogFilter { Partner = "PARTNER02" }, _ct);

        result.Should().ContainSingle().Which.PartnerLabel.Should().Be("PARTNER02");
    }

    [Fact]
    public async Task GetEventsAsync_NoFilter_ReturnsAllAscending()
    {
        await AppendAsync(EbicsEventType.UploadStarted, "AA", "BTU");
        await AppendAsync(EbicsEventType.UploadCompleted, "AA", "BTU");

        var result = await _provider.GetEventsAsync(new EventLogFilter(), _ct);

        result.Select(e => e.Sequence).Should().Equal([1, 2]);
    }

    [Fact]
    public async Task GetCustomerOptionsAsync_ReturnsDistinctPartnersAscending()
    {
        await _log.AppendAsync(Event(EbicsEventType.UploadStarted, "AA", "BTU", partner: "PARTNER02"), _ct);
        await _log.AppendAsync(Event(EbicsEventType.UploadStarted, "BB", "BTU", partner: "PARTNER01"), _ct);
        await _log.AppendAsync(Event(EbicsEventType.UploadStarted, "CC", "BTU", partner: "PARTNER02"), _ct);

        var customers = await _provider.GetCustomerOptionsAsync(_ct);

        customers.Should().Equal(["PARTNER01", "PARTNER02"]);
    }

    // --- Helpers ---------------------------------------------------------------------------

    private Task AppendAsync(
        EbicsEventType type, string transactionIdHex, string orderType,
        EbicsEventSeverity severity = EbicsEventSeverity.Info, EbicsReturnCode? returnCode = null)
        => _log.AppendAsync(
            new EbicsEvent
            {
                Type = type,
                Severity = severity,
                HostId = Subscriber.HostId,
                PartnerId = Subscriber.PartnerId,
                UserId = Subscriber.UserId,
                OrderType = orderType,
                TransactionId = transactionIdHex,
                ReturnCode = returnCode,
                Message = $"{type} {transactionIdHex}",
            },
            _ct);

    private static EbicsEvent Event(EbicsEventType type, string transactionIdHex, string orderType, string partner)
        => new()
        {
            Type = type,
            HostId = HostId.Create("EBICOHOST"),
            PartnerId = PartnerId.Create(partner),
            UserId = UserId.Create("USER01"),
            OrderType = orderType,
            TransactionId = transactionIdHex,
            Message = $"{type} {transactionIdHex}",
        };

    private static TransactionSummary Summary(IReadOnlyList<TransactionSummary> transactions, string hex)
        => transactions.Single(t => t.TransactionIdHex == hex);

    private static TransactionStatus Status(IReadOnlyList<TransactionSummary> transactions, string hex)
        => Summary(transactions, hex).Status;

    private static byte[] Id(byte value)
    {
        var id = new byte[16];
        Array.Fill(id, value);
        return id;
    }
}
