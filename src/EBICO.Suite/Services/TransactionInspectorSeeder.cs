using System.Text;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;
using EBICO.Server.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Suite.Services;

/// <summary>
/// Seeds the in-process event log, transaction stores and message capture store with a small,
/// deterministic set of sample transactions on start-up, so the transaction inspector (issue #54) has
/// content to show against the otherwise-empty in-memory stores.
/// </summary>
/// <remarks>
/// The Suite runs no live EBICS pipeline (it is a standalone admin/inspector process, ADR-0009), so the
/// data that the pipeline would normally write is seeded here instead. The cases deliberately span both
/// directions (upload/download), both outcomes (success/failure), both lifecycle states
/// (running/completed/evicted) and both visibilities (customer-visible/internal). Seeding is guarded so it
/// runs at most once.
/// </remarks>
public static class TransactionInspectorSeeder
{
    private static readonly SubscriberKeyRef Subscriber01 = new(
        HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER01"), UserId.Create("USER01"));

    private static readonly SubscriberKeyRef Subscriber02 = new(
        HostId.Create("EBICOHOST"), PartnerId.Create("PARTNER02"), UserId.Create("USER09"));

    private const string PainSample =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pain.001.001.09\">\n" +
        "  <CstmrCdtTrfInitn>\n" +
        "    <GrpHdr><MsgId>EBICO-DEMO-001</MsgId><NbOfTxs>1</NbOfTxs></GrpHdr>\n" +
        "    <PmtInf><PmtInfId>PMT-1</PmtInfId><CdtTrfTxInf><Amt><InstdAmt Ccy=\"EUR\">1234.56</InstdAmt></Amt></CdtTrfTxInf></PmtInf>\n" +
        "  </CstmrCdtTrfInitn>\n" +
        "</Document>\n";

    private const string StatementSample =
        ":20:EBICO-DEMO-STMT\n" +
        ":25:DE12500105170648489890\n" +
        ":28C:00001/001\n" +
        ":60F:C250716EUR10000,00\n" +
        ":61:2507160716C1234,56NTRFNONREF\n" +
        ":62F:C250716EUR11234,56\n";

    /// <summary>Seeds the sample transactions/events/captures into the stores resolved from <paramref name="services"/>.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the sample data has been stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var eventLog = sp.GetRequiredService<IEventLog>();
        var uploads = sp.GetRequiredService<IUploadTransactionStore>();
        var downloads = sp.GetRequiredService<IDownloadTransactionStore>();
        var captures = sp.GetRequiredService<IMessageCaptureStore>();
        var clock = sp.GetRequiredService<TimeProvider>();

        // Idempotent: seeding always creates resident transactions, so a non-empty store means seeded.
        if (uploads.Count > 0 || downloads.Count > 0)
        {
            return;
        }

        var now = clock.GetUtcNow();

        await SeedCompletedUploadAsync(eventLog, uploads, captures, now, cancellationToken).ConfigureAwait(false);
        await SeedCompletedDownloadAsync(eventLog, downloads, captures, now, cancellationToken).ConfigureAwait(false);
        await SeedRunningUploadAsync(eventLog, uploads, captures, now, cancellationToken).ConfigureAwait(false);
        await SeedFailedUploadAsync(eventLog, captures, cancellationToken).ConfigureAwait(false);
        await SeedEvictedDownloadAsync(eventLog, cancellationToken).ConfigureAwait(false);
    }

    // Case 1: an upload (BTU) that completed successfully and is still resident (order data available).
    private static async Task SeedCompletedUploadAsync(
        IEventLog eventLog, IUploadTransactionStore uploads, IMessageCaptureStore captures, DateTimeOffset now, CancellationToken ct)
    {
        var id = Id(0x11);
        var hex = Convert.ToHexString(id);

        await AppendRequestAsync(eventLog, EbicsEventType.RequestReceived, Subscriber01, "BTU", hex, EbicsReturnCode.Ok, "BTU → EBICS_OK", ct).ConfigureAwait(false);
        await eventLog.AppendAsync(Lifecycle(EbicsEventType.UploadStarted, Subscriber01, "BTU", hex, EbicsReturnCode.Ok, "Upload started (2 segment(s))"), ct).ConfigureAwait(false);
        await eventLog.AppendAsync(TransferNoise(hex), ct).ConfigureAwait(false);
        await eventLog.AppendAsync(Lifecycle(EbicsEventType.UploadCompleted, Subscriber01, "BTU", hex, EbicsReturnCode.Ok, "Upload completed (2 segment(s), 512 byte(s))"), ct).ConfigureAwait(false);

        await captures.AppendAsync(Capture(hex, EbicsTransactionPhase.Initialisation, null, "BTU", Subscriber01,
            RequestXml("ebicsRequest", "Initialisation", "BTU"), ResponseXml("ebicsResponse", "Initialisation", "000000"), EbicsReturnCode.Ok), ct).ConfigureAwait(false);
        await captures.AppendAsync(Capture(hex, EbicsTransactionPhase.Transfer, 1, "BTU", Subscriber01,
            RequestXml("ebicsRequest", "Transfer", "BTU"), ResponseXml("ebicsResponse", "Transfer", "000000"), EbicsReturnCode.Ok), ct).ConfigureAwait(false);

        var tx = new UploadTransaction(id, EbicsVersion.H005, Subscriber01, "BTU", numSegments: 2, transactionKey: new byte[16], signatureData: null, createdAt: now);
        tx.Complete(Encoding.UTF8.GetBytes(PainSample));
        uploads.Create(tx);
    }

    // Case 2: a download (BTD) that completed successfully and is still resident (order data available).
    private static async Task SeedCompletedDownloadAsync(
        IEventLog eventLog, IDownloadTransactionStore downloads, IMessageCaptureStore captures, DateTimeOffset now, CancellationToken ct)
    {
        var id = Id(0x22);
        var hex = Convert.ToHexString(id);

        await AppendRequestAsync(eventLog, EbicsEventType.RequestReceived, Subscriber01, "BTD", hex, EbicsReturnCode.Ok, "BTD → EBICS_OK", ct).ConfigureAwait(false);
        await eventLog.AppendAsync(Lifecycle(EbicsEventType.DownloadStarted, Subscriber01, "BTD", hex, EbicsReturnCode.Ok, "Download started (1 segment(s))"), ct).ConfigureAwait(false);
        await eventLog.AppendAsync(Lifecycle(EbicsEventType.DownloadCompleted, Subscriber01, "BTD", hex, EbicsReturnCode.DownloadPostprocessDone, "Download completed (positive receipt)"), ct).ConfigureAwait(false);

        await captures.AppendAsync(Capture(hex, EbicsTransactionPhase.Initialisation, 1, "BTD", Subscriber01,
            RequestXml("ebicsRequest", "Initialisation", "BTD"), ResponseXml("ebicsResponse", "Initialisation", "000000"), EbicsReturnCode.Ok), ct).ConfigureAwait(false);
        await captures.AppendAsync(Capture(hex, EbicsTransactionPhase.Receipt, null, "BTD", Subscriber01,
            RequestXml("ebicsRequest", "Receipt", "BTD"), ResponseXml("ebicsResponse", "Receipt", "011000"), EbicsReturnCode.DownloadPostprocessDone), ct).ConfigureAwait(false);

        var plaintext = Encoding.UTF8.GetBytes(StatementSample);
        var download = new DownloadTransaction(
            id, EbicsVersion.H005, Subscriber01, "BTD",
            segments: [plaintext],
            encryptedTransactionKey: new byte[16],
            encryptionPubKeyDigest: new byte[32],
            encryptionVersion: KeyVersion.Create("E002"),
            orderDataPlaintext: plaintext,
            createdAt: now);
        downloads.Create(download);
    }

    // Case 3: an upload still in flight (initialised, first segment buffered, not yet complete).
    private static async Task SeedRunningUploadAsync(
        IEventLog eventLog, IUploadTransactionStore uploads, IMessageCaptureStore captures, DateTimeOffset now, CancellationToken ct)
    {
        var id = Id(0x33);
        var hex = Convert.ToHexString(id);

        await AppendRequestAsync(eventLog, EbicsEventType.RequestReceived, Subscriber02, "BTU", hex, EbicsReturnCode.Ok, "BTU → EBICS_OK", ct).ConfigureAwait(false);
        await eventLog.AppendAsync(Lifecycle(EbicsEventType.UploadStarted, Subscriber02, "BTU", hex, EbicsReturnCode.Ok, "Upload started (3 segment(s))"), ct).ConfigureAwait(false);

        await captures.AppendAsync(Capture(hex, EbicsTransactionPhase.Initialisation, null, "BTU", Subscriber02,
            RequestXml("ebicsRequest", "Initialisation", "BTU"), ResponseXml("ebicsResponse", "Initialisation", "000000"), EbicsReturnCode.Ok), ct).ConfigureAwait(false);

        // Resident but not completed: OrderData stays null, so the detail view shows "not yet available".
        var tx = new UploadTransaction(id, EbicsVersion.H005, Subscriber02, "BTU", numSegments: 3, transactionKey: new byte[16], signatureData: null, createdAt: now);
        uploads.Create(tx);
    }

    // Case 4: an upload rejected in the initialisation phase (invalid order data). Not resident.
    private static async Task SeedFailedUploadAsync(IEventLog eventLog, IMessageCaptureStore captures, CancellationToken ct)
    {
        var id = Id(0x44);
        var hex = Convert.ToHexString(id);

        await eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = EbicsEventType.RequestReceived,
                Severity = EbicsEventSeverity.Warning,
                Visibility = EbicsEventVisibility.CustomerVisible,
                HostId = Subscriber02.HostId,
                PartnerId = Subscriber02.PartnerId,
                UserId = Subscriber02.UserId,
                OrderType = "BTU",
                TransactionId = hex,
                ReturnCode = EbicsReturnCode.InvalidOrderDataFormat,
                Message = "BTU → EBICS_INVALID_ORDER_DATA_FORMAT",
            },
            ct).ConfigureAwait(false);
        await eventLog.AppendAsync(Lifecycle(EbicsEventType.OrderRejected, Subscriber02, "BTU", hex, EbicsReturnCode.InvalidOrderDataFormat, "Order rejected: order data is not well-formed", EbicsEventSeverity.Warning), ct).ConfigureAwait(false);

        await captures.AppendAsync(Capture(hex, EbicsTransactionPhase.Initialisation, null, "BTU", Subscriber02,
            RequestXml("ebicsRequest", "Initialisation", "BTU"), ResponseXml("ebicsResponse", "Initialisation", "090004"), EbicsReturnCode.InvalidOrderDataFormat), ct).ConfigureAwait(false);
    }

    // Case 5: a download that was started and then evicted by the idle-timeout sweep. Not resident.
    private static async Task SeedEvictedDownloadAsync(IEventLog eventLog, CancellationToken ct)
    {
        var id = Id(0x55);
        var hex = Convert.ToHexString(id);

        await AppendRequestAsync(eventLog, EbicsEventType.RequestReceived, Subscriber01, "BTD", hex, EbicsReturnCode.Ok, "BTD → EBICS_OK", ct).ConfigureAwait(false);
        await eventLog.AppendAsync(Lifecycle(EbicsEventType.DownloadStarted, Subscriber01, "BTD", hex, EbicsReturnCode.Ok, "Download started (1 segment(s))"), ct).ConfigureAwait(false);
        await eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = EbicsEventType.TransactionEvicted,
                Severity = EbicsEventSeverity.Warning,
                Visibility = EbicsEventVisibility.Internal,
                HostId = Subscriber01.HostId,
                PartnerId = Subscriber01.PartnerId,
                UserId = Subscriber01.UserId,
                OrderType = "BTD",
                TransactionId = hex,
                Message = "Download transaction evicted after idle timeout",
            },
            ct).ConfigureAwait(false);
    }

    private static Task AppendRequestAsync(
        IEventLog eventLog, EbicsEventType type, SubscriberKeyRef subscriber, string orderType, string hex,
        EbicsReturnCode returnCode, string message, CancellationToken ct)
        => eventLog.AppendAsync(
            new EbicsEvent
            {
                Type = type,
                Severity = EbicsEventSeverity.Info,
                Visibility = EbicsEventVisibility.CustomerVisible,
                HostId = subscriber.HostId,
                PartnerId = subscriber.PartnerId,
                UserId = subscriber.UserId,
                OrderType = orderType,
                TransactionId = hex,
                ReturnCode = returnCode,
                Message = message,
            },
            ct);

    private static EbicsEvent Lifecycle(
        EbicsEventType type, SubscriberKeyRef subscriber, string orderType, string hex, EbicsReturnCode returnCode,
        string message, EbicsEventSeverity severity = EbicsEventSeverity.Info)
        => new()
        {
            Type = type,
            Severity = severity,
            Visibility = EbicsEventVisibility.CustomerVisible,
            HostId = subscriber.HostId,
            PartnerId = subscriber.PartnerId,
            UserId = subscriber.UserId,
            OrderType = orderType,
            TransactionId = hex,
            ReturnCode = returnCode,
            Message = message,
        };

    // A transfer-phase per-request event: operator-internal segment noise carrying only the host id.
    private static EbicsEvent TransferNoise(string hex)
        => new()
        {
            Type = EbicsEventType.RequestReceived,
            Severity = EbicsEventSeverity.Info,
            Visibility = EbicsEventVisibility.Internal,
            HostId = Subscriber01.HostId,
            TransactionId = hex,
            ReturnCode = EbicsReturnCode.Ok,
            Message = "request → EBICS_OK",
        };

    private static CapturedMessage Capture(
        string hex, EbicsTransactionPhase phase, int? segment, string orderType, SubscriberKeyRef subscriber,
        string requestXml, string responseXml, EbicsReturnCode returnCode)
        => new()
        {
            TransactionIdHex = hex,
            Phase = phase,
            SegmentNumber = segment,
            OrderType = orderType,
            HostId = subscriber.HostId,
            PartnerId = subscriber.PartnerId,
            UserId = subscriber.UserId,
            RequestXml = requestXml,
            ResponseXml = responseXml,
            ReturnCode = returnCode,
        };

    private static byte[] Id(byte value)
    {
        var id = new byte[16];
        Array.Fill(id, value);
        return id;
    }

    private static string RequestXml(string root, string phase, string orderType) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        $"<{root} xmlns=\"urn:org:ebics:H005\" Version=\"H005\">\n" +
        $"  <header authenticate=\"true\">\n" +
        $"    <mutable><TransactionPhase>{phase}</TransactionPhase></mutable>\n" +
        $"    <static><OrderDetails><AdminOrderType>{orderType}</AdminOrderType></OrderDetails></static>\n" +
        $"  </header>\n" +
        $"</{root}>\n";

    private static string ResponseXml(string root, string phase, string returnCode) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        $"<{root} xmlns=\"urn:org:ebics:H005\" Version=\"H005\">\n" +
        $"  <header authenticate=\"true\">\n" +
        $"    <mutable><TransactionPhase>{phase}</TransactionPhase><ReturnCode>{returnCode}</ReturnCode></mutable>\n" +
        $"    <static/>\n" +
        $"  </header>\n" +
        $"  <body><ReturnCode>{returnCode}</ReturnCode></body>\n" +
        $"</{root}>\n";
}
