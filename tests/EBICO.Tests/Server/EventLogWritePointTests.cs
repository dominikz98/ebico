using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests that the wired write points (issue #69) actually emit events end-to-end: the central per-request
/// event from <see cref="EbicsRequestPipeline"/> (with its visibility mapping) and the transaction-engine
/// lifecycle events (upload/download started/completed). Driven through the real pipeline over a DI
/// provider, then asserted by reading back from the resolved <see cref="IEventLog"/>.
/// </summary>
public class EventLogWritePointTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    // --- Central per-request event ---------------------------------------------------------

    [Fact]
    public async Task Pipeline_UnknownOrderType_WritesRequestReceivedWithReturnCode()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IEbicsRequestPipeline>();
        var log = provider.GetRequiredService<IEventLog>();

        await pipeline.ProcessAsync(ServerTestHelpers.BuildH004Request("AAA"), _ct);

        var events = await log.QueryAsync(new EbicsEventQuery { Type = EbicsEventType.RequestReceived }, _ct);
        var evt = events.Should().ContainSingle().Subject;
        evt.OrderType.Should().Be("AAA");
        evt.ReturnCode!.Value.SymbolicName.Should().Be("EBICS_UNSUPPORTED_ORDER_TYPE");
        evt.Severity.Should().Be(EbicsEventSeverity.Warning);
    }

    // --- Upload lifecycle ------------------------------------------------------------------

    [Fact]
    public async Task Upload_EmitsStartedAndCompleted_AndRequestEventsWithPhaseVisibility()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IEbicsRequestPipeline>();
        var master = provider.GetRequiredService<IMasterDataManager>();
        var bankKeys = provider.GetRequiredService<IServerBankKeyStore>();
        var log = provider.GetRequiredService<IEventLog>();

        await SeedReadySubscriberAsync(master);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var orderData = Encoding.UTF8.GetBytes("<order>hello ebics upload</order>");
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H004, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion);

        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));
        var transferXml = ServerTestHelpers.BuildUploadTransferRequest(
            EbicsVersion.H004, Host, transactionId!, segmentNumber: 1, lastSegment: true, segment: upload.Segments[0]);
        await pipeline.ProcessAsync(transferXml, _ct);

        var hexId = Convert.ToHexString(transactionId!);

        // Lifecycle events carry the full subscriber triple, order type and transaction id.
        var started = (await log.QueryAsync(new EbicsEventQuery { Type = EbicsEventType.UploadStarted }, _ct)).Should().ContainSingle().Subject;
        started.PartnerId.Should().Be(PartnerId.Create(Partner));
        started.UserId.Should().Be(UserId.Create(User));
        started.OrderType.Should().Be("FUL");
        started.TransactionId.Should().Be(hexId);
        started.Visibility.Should().Be(EbicsEventVisibility.CustomerVisible);

        (await log.QueryAsync(new EbicsEventQuery { Type = EbicsEventType.UploadCompleted }, _ct))
            .Should().ContainSingle().Which.TransactionId.Should().Be(hexId);

        // The initialisation request is customer-visible; the transfer request is an internal segment step.
        var initReq = (await log.QueryAsync(
            new EbicsEventQuery { Type = EbicsEventType.RequestReceived, Visibility = EbicsEventVisibility.CustomerVisible }, _ct))
            .Should().ContainSingle().Subject;
        initReq.OrderType.Should().Be("FUL");

        (await log.QueryAsync(
            new EbicsEventQuery { Type = EbicsEventType.RequestReceived, Visibility = EbicsEventVisibility.Internal }, _ct))
            .Should().ContainSingle();
    }

    // --- Download lifecycle ----------------------------------------------------------------

    [Fact]
    public async Task Download_PositiveReceipt_EmitsStartedAndCompleted()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IEbicsRequestPipeline>();
        var master = provider.GetRequiredService<IMasterDataManager>();
        var keys = provider.GetRequiredService<IServerKeyStore>();
        var dataProvider = provider.GetRequiredService<IDownloadDataProvider>();
        var log = provider.GetRequiredService<IEventLog>();

        var subscriberEnc = RsaKeyMaterial.Generate();
        await SeedReadySubscriberAsync(master);
        await keys.StoreAsync(KeyRef(), new StoredPublicKey(subscriberEnc.ToPublicOnly(), KeyVersion.Create("E002")), _ct);
        await dataProvider.EnqueueAsync(KeyRef(), "FDL", Encoding.UTF8.GetBytes("<order>download</order>"), _ct);

        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(
            await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H004, Host, Partner, User), _ct)));
        await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(EbicsVersion.H004, Host, transactionId!, receiptCode: 0), _ct);

        var hexId = Convert.ToHexString(transactionId!);

        (await log.QueryAsync(new EbicsEventQuery { Type = EbicsEventType.DownloadStarted }, _ct))
            .Should().ContainSingle().Which.TransactionId.Should().Be(hexId);
        var completed = (await log.QueryAsync(new EbicsEventQuery { Type = EbicsEventType.DownloadCompleted }, _ct)).Should().ContainSingle().Subject;
        completed.OrderType.Should().Be("FDL");
        completed.ReturnCode!.Value.SymbolicName.Should().Be("EBICS_DOWNLOAD_POSTPROCESS_DONE");
    }

    [Fact]
    public async Task Download_NegativeReceipt_EmitsReceiptNegative()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IEbicsRequestPipeline>();
        var master = provider.GetRequiredService<IMasterDataManager>();
        var keys = provider.GetRequiredService<IServerKeyStore>();
        var dataProvider = provider.GetRequiredService<IDownloadDataProvider>();
        var log = provider.GetRequiredService<IEventLog>();

        var subscriberEnc = RsaKeyMaterial.Generate();
        await SeedReadySubscriberAsync(master);
        await keys.StoreAsync(KeyRef(), new StoredPublicKey(subscriberEnc.ToPublicOnly(), KeyVersion.Create("E002")), _ct);
        await dataProvider.EnqueueAsync(KeyRef(), "FDL", Encoding.UTF8.GetBytes("<order>download</order>"), _ct);

        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(
            await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H004, Host, Partner, User), _ct)));
        await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(EbicsVersion.H004, Host, transactionId!, receiptCode: 1), _ct);

        var negative = (await log.QueryAsync(new EbicsEventQuery { Type = EbicsEventType.ReceiptNegative }, _ct)).Should().ContainSingle().Subject;
        negative.Severity.Should().Be(EbicsEventSeverity.Warning);
        negative.ReturnCode!.Value.SymbolicName.Should().Be("EBICS_DOWNLOAD_POSTPROCESS_SKIPPED");
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static SubscriberKeyRef KeyRef()
        => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

    private async Task SeedReadySubscriberAsync(IMasterDataManager master)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
