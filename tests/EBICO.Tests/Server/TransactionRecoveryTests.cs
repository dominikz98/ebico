using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using EBICO.Server.Transactions;
using EBICO.Tests.Connector;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for transaction recovery &amp; timeouts (issue #35): idle expiry of transaction ids (lazy on
/// access and via the background evictor), the sliding activity window, the concurrent-transaction
/// ceiling, and the idempotency/replay behavior within the retention window. Driven end-to-end through
/// <see cref="EbicsRequestPipeline"/> with a <see cref="MutableTimeProvider"/> so time can be advanced
/// deterministically; the background sweeper is exercised by calling <see cref="ITransactionEvictor"/>
/// directly (no timer plumbing).
/// </summary>
public class TransactionRecoveryTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";
    private const EbicsVersion Version = EbicsVersion.H004;

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    // --- Upload: idle expiry ---------------------------------------------------------------

    [Fact]
    public async Task UploadTransfer_AfterTimeout_ReturnsTxUnknownTxid_AndRemoves()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10));
        var (pipeline, master, bankKeys, store) = UploadServices(provider);
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, Encoding.UTF8.GetBytes("data"), bank.Encryption, bank.EncryptionVersion);
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));
        store.Count.Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(11));

        var transferXml = ServerTestHelpers.BuildUploadTransferRequest(Version, Host, transactionId!, 1, true, upload.Segments[0]);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(transferXml, _ct))).BodyCode.Should().Be("091101");
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task UploadTransfer_SlidingWindow_ActiveMultiSegment_NotEvicted()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10), segmentSizeBytes: 32);
        var (pipeline, master, bankKeys, store) = UploadServices(provider);
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion, segmentSizeBytes: 32);
        upload.Segments.Count.Should().BeGreaterThan(2);
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        // Each gap (6 min) is below the 10-min timeout, but the total elapsed time exceeds it: the sliding
        // window keeps the transaction alive as long as activity keeps arriving.
        for (var i = 0; i < upload.Segments.Count; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(6));
            var last = i == upload.Segments.Count - 1;
            var xml = ServerTestHelpers.BuildUploadTransferRequest(Version, Host, transactionId!, (ulong)(i + 1), last, upload.Segments[i]);
            ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(xml, _ct))).Should().Be(("000000", "000000"));
        }

        store.TryGet(Convert.ToHexString(transactionId!), out var transaction).Should().BeTrue();
        transaction!.OrderData.Should().Equal(orderData);
    }

    [Fact]
    public async Task UploadTransfer_ReplayWithinRetention_ReturnsTxMessageReplay()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10), segmentSizeBytes: 32);
        var (pipeline, master, bankKeys, _) = UploadServices(provider);
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion, segmentSizeBytes: 32);
        upload.Segments.Count.Should().BeGreaterThan(1);
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        var first = ServerTestHelpers.BuildUploadTransferRequest(Version, Host, transactionId!, 1, false, upload.Segments[0]);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(first, _ct))).Should().Be(("000000", "000000"));

        // Still within the retention window: the replayed segment is recognised as a replay (091103),
        // not treated as an unknown transaction.
        clock.Advance(TimeSpan.FromMinutes(1));
        var replay = ServerTestHelpers.BuildUploadTransferRequest(Version, Host, transactionId!, 1, false, upload.Segments[0]);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(replay, _ct))).BodyCode.Should().Be("091103");
    }

    [Fact]
    public async Task UploadTransfer_ReplayAfterRetentionExpired_ReturnsTxUnknownTxid()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10), segmentSizeBytes: 32);
        var (pipeline, master, bankKeys, _) = UploadServices(provider);
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion, segmentSizeBytes: 32);
        upload.Segments.Count.Should().BeGreaterThan(1);
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        var first = ServerTestHelpers.BuildUploadTransferRequest(Version, Host, transactionId!, 1, false, upload.Segments[0]);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(first, _ct))).Should().Be(("000000", "000000"));

        // After the retention window elapses, the same replay is answered as an unknown transaction (091101).
        clock.Advance(TimeSpan.FromMinutes(11));
        var replay = ServerTestHelpers.BuildUploadTransferRequest(Version, Host, transactionId!, 1, false, upload.Segments[0]);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(replay, _ct))).BodyCode.Should().Be("091101");
    }

    // --- Upload: concurrent-transaction ceiling --------------------------------------------

    [Fact]
    public async Task UploadInit_WhenMaxConcurrentReached_ReturnsMaxTransactionsExceeded()
    {
        var (provider, _) = BuildServer(maxConcurrent: 1);
        var (pipeline, master, bankKeys, store) = UploadServices(provider);
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var first = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, Encoding.UTF8.GetBytes("one"), bank.Encryption, bank.EncryptionVersion);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(first.InitXml, _ct))).Should().Be(("000000", "000000"));

        var second = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, Encoding.UTF8.GetBytes("two"), bank.Encryption, bank.EncryptionVersion);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(second.InitXml, _ct))).BodyCode.Should().Be("091115");
        store.Count.Should().Be(1);
    }

    // --- Upload: background evictor ---------------------------------------------------------

    [Fact]
    public async Task UploadEvictExpired_RemovesExpired_KeepsActive()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10));
        var (pipeline, master, bankKeys, store) = UploadServices(provider);
        var evictor = (ITransactionEvictor)provider.GetRequiredService<IUploadTransactionEngine>();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var older = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, Encoding.UTF8.GetBytes("older"), bank.Encryption, bank.EncryptionVersion);
        var olderId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(older.InitXml, _ct)));

        clock.Advance(TimeSpan.FromMinutes(5));
        var newer = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, Encoding.UTF8.GetBytes("newer"), bank.Encryption, bank.EncryptionVersion);
        var newerId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(newer.InitXml, _ct)));

        // At +12 min the older (created at 0) has expired; the newer (created at +5) has not.
        clock.Advance(TimeSpan.FromMinutes(7));
        (await evictor.EvictExpiredAsync(_ct)).Should().Be(1);

        store.Count.Should().Be(1);
        store.TryGet(Convert.ToHexString(olderId!), out _).Should().BeFalse();
        store.TryGet(Convert.ToHexString(newerId!), out _).Should().BeTrue();
    }

    [Fact]
    public async Task UploadEvictExpired_WhenTimeoutDisabled_RemovesNothing()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.Zero);
        var (pipeline, master, bankKeys, store) = UploadServices(provider);
        var evictor = (ITransactionEvictor)provider.GetRequiredService<IUploadTransactionEngine>();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            Version, Host, Partner, User, Encoding.UTF8.GetBytes("data"), bank.Encryption, bank.EncryptionVersion);
        await pipeline.ProcessAsync(upload.InitXml, _ct);

        clock.Advance(TimeSpan.FromDays(365));
        (await evictor.EvictExpiredAsync(_ct)).Should().Be(0);
        store.Count.Should().Be(1);
    }

    // --- Download: idle expiry re-enqueues data --------------------------------------------

    [Fact]
    public async Task DownloadTransfer_AfterTimeout_ReturnsTxUnknownTxid_AndReenqueues()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10), segmentSizeBytes: 32);
        var (pipeline, master, keys, dataProvider, store) = DownloadServices(provider);
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());

        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        await dataProvider.EnqueueAsync(KeyRef(), "FDL", orderData, _ct);

        var initEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(Version, Host, Partner, User), _ct));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        ServerTestHelpers.ReadNumSegments(initEnvelope).Should().BeGreaterThan(1UL);
        (await dataProvider.CountAsync(KeyRef(), "FDL", _ct)).Should().Be(0); // dequeued by the init

        clock.Advance(TimeSpan.FromMinutes(11));

        // A transfer for segment 2 routes to the download engine (OwnsTransaction), which evicts the
        // expired transaction, re-enqueues its order data exactly once, and answers 091101.
        var xml = ServerTestHelpers.BuildDownloadTransferRequest(Version, Host, transactionId!, 2, false);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(xml, _ct))).BodyCode.Should().Be("091101");
        store.Count.Should().Be(0);
        (await dataProvider.CountAsync(KeyRef(), "FDL", _ct)).Should().Be(1); // re-enqueued
    }

    [Fact]
    public async Task DownloadEvictExpired_RemovesExpired_AndReenqueuesOnce()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10));
        var (pipeline, master, keys, dataProvider, store) = DownloadServices(provider);
        var evictor = (ITransactionEvictor)provider.GetRequiredService<IDownloadTransactionEngine>();
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());
        await dataProvider.EnqueueAsync(KeyRef(), "FDL", Encoding.UTF8.GetBytes("data"), _ct);

        await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(Version, Host, Partner, User), _ct);
        store.Count.Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(11));

        (await evictor.EvictExpiredAsync(_ct)).Should().Be(1);
        // Running it again is a no-op — the data is re-enqueued exactly once (Remove is the guard).
        (await evictor.EvictExpiredAsync(_ct)).Should().Be(0);

        store.Count.Should().Be(0);
        (await dataProvider.CountAsync(KeyRef(), "FDL", _ct)).Should().Be(1);
    }

    // --- Download: receipt honoured even past the timeout, and idempotent after removal ----

    [Fact]
    public async Task DownloadReceipt_AfterTimeoutButStillPresent_HonorsReceipt()
    {
        var (provider, clock) = BuildServer(timeout: TimeSpan.FromMinutes(10));
        var (pipeline, master, keys, dataProvider, store) = DownloadServices(provider);
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());
        await dataProvider.EnqueueAsync(KeyRef(), "FDL", Encoding.UTF8.GetBytes("data"), _ct);

        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(
            await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(Version, Host, Partner, User), _ct)));

        // The client did receive the data; the receipt is honoured even though the idle window elapsed
        // (the receipt phase does not apply expiry). The data stays consumed.
        clock.Advance(TimeSpan.FromMinutes(11));
        var receipt = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(Version, Host, transactionId!, 0), _ct));
        ServerTestHelpers.ReadReturnCodes(receipt).Should().Be(("011000", "000000"));
        store.Count.Should().Be(0);
        (await dataProvider.CountAsync(KeyRef(), "FDL", _ct)).Should().Be(0);
    }

    [Fact]
    public async Task DownloadReceipt_RepeatedAfterRemoval_ReturnsTxUnknownTxid()
    {
        var (provider, _) = BuildServer();
        var (pipeline, master, keys, dataProvider, _) = DownloadServices(provider);
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());
        await dataProvider.EnqueueAsync(KeyRef(), "FDL", Encoding.UTF8.GetBytes("data"), _ct);

        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(
            await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(Version, Host, Partner, User), _ct)));

        var first = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(Version, Host, transactionId!, 0), _ct));
        ServerTestHelpers.ReadReturnCodes(first).HeaderCode.Should().Be("011000");

        // The transaction was removed by the first receipt; a repeated receipt is an unknown id.
        var second = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(Version, Host, transactionId!, 0), _ct));
        ServerTestHelpers.ReadReturnCodes(second).BodyCode.Should().Be("091101");
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (ServiceProvider Provider, MutableTimeProvider Clock) BuildServer(
        TimeSpan? timeout = null, int maxConcurrent = 0, int? segmentSizeBytes = null)
    {
        var clock = new MutableTimeProvider(Start);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddEbicoServer(o =>
        {
            if (timeout is { } t)
            {
                o.TransactionTimeout = t;
            }

            o.MaxConcurrentTransactions = maxConcurrent;

            if (segmentSizeBytes is { } size)
            {
                o.SegmentSizeBytes = size;
            }
        });

        return (services.BuildServiceProvider(), clock);
    }

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerBankKeyStore BankKeys, IUploadTransactionStore Store) UploadServices(ServiceProvider provider)
        => (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerBankKeyStore>(),
            provider.GetRequiredService<IUploadTransactionStore>());

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerKeyStore Keys, IDownloadDataProvider Provider, IDownloadTransactionStore Store) DownloadServices(ServiceProvider provider)
        => (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerKeyStore>(),
            provider.GetRequiredService<IDownloadDataProvider>(),
            provider.GetRequiredService<IDownloadTransactionStore>());

    private static SubscriberKeyRef KeyRef()
        => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

    private async Task SeedSubscriberAsync(IMasterDataManager master, SubscriberState state)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);

        // Generic upload/download authorisation (issue #38): the engines require a permission for the
        // requested order type. Transitions preserve the permission set.
        await master.SaveSubscriberAsync(
            new Subscriber(host, partner, user, permissions:
            [
                new SubscriberPermission("FUL", SignatureClass.T),
                new SubscriberPermission("BTU", SignatureClass.T),
                new SubscriberPermission("FDL", SignatureClass.T),
                new SubscriberPermission("BTD", SignatureClass.T),
            ]),
            _ct);

        if (state is SubscriberState.Initialized or SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        }

        if (state == SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
        }
    }

    private async Task SeedReadySubscriberAsync(IMasterDataManager master, IServerKeyStore keys, RsaKeyMaterial encryptionPublicKey)
    {
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        await keys.StoreAsync(KeyRef(), new StoredPublicKey(encryptionPublicKey, KeyVersion.Create("E002")), _ct);
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
