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
/// Tests for the download transaction engine (issue #33): the three-phase EBICS download
/// (Initialisation + Transfer + Receipt). The initialisation provisions the order data, compresses,
/// E002-encrypts and segments it, assigns a transaction id and returns the first segment; the transfer
/// phase serves the remaining segments; the receipt phase processes the positive/negative
/// acknowledgement (consuming semantics: a positive receipt consumes the data, a negative one
/// re-enqueues it). Driven end-to-end through <see cref="EbicsRequestPipeline"/>; requests are built
/// from the committed Core bindings (no proprietary fixtures) and proven by decrypting the delivered
/// data with the subscriber's private encryption key.
/// </summary>
public class DownloadTransactionTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public static TheoryData<EbicsVersion> AllVersions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    // --- Happy path: single segment --------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Download_SingleSegment_DeliversDataAndReceiptConsumes(EbicsVersion version)
    {
        var (pipeline, master, keys, provider, store) = BuildServer();
        var subscriberEnc = RsaKeyMaterial.Generate();
        await SeedReadySubscriberAsync(master, keys, subscriberEnc.ToPublicOnly());

        var orderData = Encoding.UTF8.GetBytes("<order>hello ebics download</order>");
        await provider.EnqueueAsync(KeyRef(), DownloadOrderType(version), orderData, _ct);

        // Initialisation: assigns the transaction id, announces one segment and carries it inline.
        var initEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));
        ServerTestHelpers.ReadReturnCodes(initEnvelope).Should().Be(("000000", "000000"));
        ServerTestHelpers.ReadTransactionPhase(initEnvelope).Should().Be("Initialisation");
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        transactionId.Should().NotBeNull().And.HaveCount(16);
        ServerTestHelpers.ReadNumSegments(initEnvelope).Should().Be(1UL);
        var (segValue, last) = ServerTestHelpers.ReadSegmentNumber(initEnvelope);
        segValue.Should().Be(1UL);
        last.Should().BeTrue();

        // The delivered segment decrypts (with the subscriber's private key) and decompresses to the original.
        var (txKey, segment, digestVersion, digest) = ServerTestHelpers.ReadDownloadDataTransfer(version, initEnvelope);
        txKey.Should().NotBeNull();
        segment.Should().NotBeNull();
        digestVersion.Should().Be("E002");
        digest.Should().Equal(PublicKeyFingerprint.Compute(subscriberEnc.ToPublicOnly()));
        var decrypted = EncryptionE002.Decrypt(new EncryptedOrderData(txKey!, segment!), subscriberEnc, KeyVersion.Create("E002"));
        EbicsCompression.Decompress(decrypted).Should().Equal(orderData);

        // Positive receipt: post-processing done (011000 technical -> header), transaction removed.
        var receiptEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(version, Host, transactionId!, 0), _ct));
        ServerTestHelpers.ReadReturnCodes(receiptEnvelope).Should().Be(("011000", "000000"));
        ServerTestHelpers.ReadTransactionPhase(receiptEnvelope).Should().Be("Receipt");
        store.Count.Should().Be(0);

        // The data was consumed: a fresh download finds nothing available.
        var againEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));
        ServerTestHelpers.ReadReturnCodes(againEnvelope).BodyCode.Should().Be("090005");
    }

    // --- Happy path: multiple segments -----------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Download_MultipleSegments_DeliversAllSegments(EbicsVersion version)
    {
        var (pipeline, master, keys, provider, _) = BuildServer(segmentSizeBytes: 32);
        var subscriberEnc = RsaKeyMaterial.Generate();
        await SeedReadySubscriberAsync(master, keys, subscriberEnc.ToPublicOnly());

        // Poorly compressible payload + a small segment size forces several segments.
        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        await provider.EnqueueAsync(KeyRef(), DownloadOrderType(version), orderData, _ct);

        var initEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        var numSegments = ServerTestHelpers.ReadNumSegments(initEnvelope);
        numSegments.Should().NotBeNull().And.BeGreaterThan(1UL);

        // The initialisation segment carries the encrypted transaction key; transfer segments do not.
        var (txKey, firstSegment, _, _) = ServerTestHelpers.ReadDownloadDataTransfer(version, initEnvelope);
        txKey.Should().NotBeNull();
        ServerTestHelpers.ReadSegmentNumber(initEnvelope).Should().Be((1UL, false));

        var ciphertext = new List<byte[]> { firstSegment! };
        for (var segmentNumber = 2UL; segmentNumber <= numSegments; segmentNumber++)
        {
            var last = segmentNumber == numSegments;
            var transferEnvelope = Deserialize(await pipeline.ProcessAsync(
                ServerTestHelpers.BuildDownloadTransferRequest(version, Host, transactionId!, segmentNumber, last), _ct));
            ServerTestHelpers.ReadReturnCodes(transferEnvelope).Should().Be(("000000", "000000"));
            ServerTestHelpers.ReadTransactionPhase(transferEnvelope).Should().Be("Transfer");
            ServerTestHelpers.ReadSegmentNumber(transferEnvelope).Should().Be((segmentNumber, last));

            var (transferKey, transferSegment, _, _) = ServerTestHelpers.ReadDownloadDataTransfer(version, transferEnvelope);
            transferKey.Should().BeNull(); // DataEncryptionInfo only on the initialisation segment
            transferSegment.Should().NotBeNull();
            ciphertext.Add(transferSegment!);
        }

        // Reassembled, decrypted and decompressed, all segments together reproduce the original.
        var reassembled = EbicsSegmentation.Reassemble(ciphertext);
        var decrypted = EncryptionE002.Decrypt(new EncryptedOrderData(txKey!, reassembled), subscriberEnc, KeyVersion.Create("E002"));
        EbicsCompression.Decompress(decrypted).Should().Equal(orderData);
    }

    // --- Negative: no data available (090005) ----------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Init_WhenNoDataAvailable_ReturnsNoDownloadDataAvailable(EbicsVersion version)
    {
        var (pipeline, master, keys, _, store) = BuildServer();
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());

        // Ready subscriber with an encryption key, but nothing enqueued for download.
        var envelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));

        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("090005");
        store.Count.Should().Be(0); // no transaction was created
    }

    // --- Negative: subscriber not ready (091002) -------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Init_WhenSubscriberNotReady_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, _, provider, store) = BuildServer();
        // INI done but not HIA -> still Initialized; a download is not yet allowed.
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        await provider.EnqueueAsync(KeyRef(), DownloadOrderType(version), Encoding.UTF8.GetBytes("data"), _ct);

        var envelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));

        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091002");
        store.Count.Should().Be(0);
    }

    // --- Negative: unknown transaction id (091101) -----------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_WithUnknownTransactionId_ReturnsTxUnknownTxid(EbicsVersion version)
    {
        var (pipeline, _, _, _, _) = BuildServer();
        var unknownTxId = Enumerable.Repeat((byte)0xCD, 16).ToArray();
        var xml = ServerTestHelpers.BuildDownloadTransferRequest(version, Host, unknownTxId, segmentNumber: 1, lastSegment: true);

        var envelope = Deserialize(await pipeline.ProcessAsync(xml, _ct));

        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091101");
    }

    // --- Negative: segment number beyond NumSegments (091104) ------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_SegmentNumberBeyondNumSegments_ReturnsTxSegmentNumberExceeded(EbicsVersion version)
    {
        var (pipeline, master, keys, provider, _) = BuildServer();
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());
        await provider.EnqueueAsync(KeyRef(), DownloadOrderType(version), Encoding.UTF8.GetBytes("data"), _ct);

        var initEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));
        ServerTestHelpers.ReadNumSegments(initEnvelope).Should().Be(1UL);
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);

        // Segment 2 does not exist for a one-segment transaction.
        var xml = ServerTestHelpers.BuildDownloadTransferRequest(version, Host, transactionId!, segmentNumber: 2, lastSegment: true);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(xml, _ct))).BodyCode.Should().Be("091104");
    }

    // --- Negative: segment number zero (091112) --------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_SegmentNumberZero_ReturnsInvalidRequestContent(EbicsVersion version)
    {
        var (pipeline, master, keys, provider, _) = BuildServer();
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());
        await provider.EnqueueAsync(KeyRef(), DownloadOrderType(version), Encoding.UTF8.GetBytes("data"), _ct);

        var initEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);

        var xml = ServerTestHelpers.BuildDownloadTransferRequest(version, Host, transactionId!, segmentNumber: 0, lastSegment: false);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(xml, _ct))).BodyCode.Should().Be("091112");
    }

    // --- Negative: receipt for unknown transaction id (091101) -----------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Receipt_WithUnknownTransactionId_ReturnsTxUnknownTxid(EbicsVersion version)
    {
        var (pipeline, _, _, _, _) = BuildServer();
        var unknownTxId = Enumerable.Repeat((byte)0xEF, 16).ToArray();

        var envelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(version, Host, unknownTxId, 0), _ct));

        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091101");
    }

    // --- Negative receipt re-enqueues the data ---------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Receipt_Negative_ReturnsPostprocessSkipped_AndReenqueuesData(EbicsVersion version)
    {
        var (pipeline, master, keys, provider, store) = BuildServer();
        await SeedReadySubscriberAsync(master, keys, RsaKeyMaterial.Generate().ToPublicOnly());
        await provider.EnqueueAsync(KeyRef(), DownloadOrderType(version), Encoding.UTF8.GetBytes("data"), _ct);

        var initEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);

        // Negative receipt: post-processing skipped (011001 technical -> header), transaction removed.
        var receiptEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadReceiptRequest(version, Host, transactionId!, 1), _ct));
        ServerTestHelpers.ReadReturnCodes(receiptEnvelope).Should().Be(("011001", "000000"));
        store.Count.Should().Be(0);

        // The data is available again for a fresh download.
        (await provider.CountAsync(KeyRef(), DownloadOrderType(version), _ct)).Should().Be(1);
        var againEnvelope = Deserialize(await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct));
        ServerTestHelpers.ReadReturnCodes(againEnvelope).Should().Be(("000000", "000000"));
    }

    // --- Routing: upload and download transactions are disambiguated on transfer -----------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_RoutesToTheCorrectEngine_ForConcurrentUploadAndDownload(EbicsVersion version)
    {
        var provider2 = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        var pipeline = provider2.GetRequiredService<IEbicsRequestPipeline>();
        var master = provider2.GetRequiredService<IMasterDataManager>();
        var keys = provider2.GetRequiredService<IServerKeyStore>();
        var bankKeys = provider2.GetRequiredService<IServerBankKeyStore>();
        var dataProvider = provider2.GetRequiredService<IDownloadDataProvider>();
        var uploadStore = provider2.GetRequiredService<IUploadTransactionStore>();

        var subscriberEnc = RsaKeyMaterial.Generate();
        await SeedReadySubscriberAsync(master, keys, subscriberEnc.ToPublicOnly());
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var downloadData = Encoding.UTF8.GetBytes("<order>download payload</order>");
        await dataProvider.EnqueueAsync(KeyRef(), DownloadOrderType(version), downloadData, _ct);

        // Start a download transaction and an upload transaction in parallel.
        var downloadTxId = ServerTestHelpers.ReadTransactionId(Deserialize(
            await pipeline.ProcessAsync(ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User), _ct)));
        var uploadOrderData = Encoding.UTF8.GetBytes("<order>upload payload</order>");
        var upload = ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, uploadOrderData, bank.Encryption, bank.EncryptionVersion);
        var uploadTxId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        // A transfer for the download id must reach the download engine (a served OrderData segment,
        // not an upload's InvalidRequestContent for the missing request order data).
        var downloadTransfer = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadTransferRequest(version, Host, downloadTxId!, 1, true), _ct));
        ServerTestHelpers.ReadReturnCodes(downloadTransfer).Should().Be(("000000", "000000"));
        ServerTestHelpers.ReadDownloadDataTransfer(version, downloadTransfer).OrderData.Should().NotBeNull();

        // A transfer for the upload id must reach the upload engine (completes and stores the order data).
        var uploadTransfer = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildUploadTransferRequest(version, Host, uploadTxId!, 1, true, upload.Segments[0]), _ct));
        ServerTestHelpers.ReadReturnCodes(uploadTransfer).Should().Be(("000000", "000000"));
        uploadStore.TryGet(Convert.ToHexString(uploadTxId!), out var uploadTransaction).Should().BeTrue();
        uploadTransaction!.OrderData.Should().Equal(uploadOrderData);
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerKeyStore Keys, IDownloadDataProvider Provider, IDownloadTransactionStore Store) BuildServer(int? segmentSizeBytes = null)
    {
        var services = new ServiceCollection();
        if (segmentSizeBytes is { } size)
        {
            services.AddEbicoServer(o => o.SegmentSizeBytes = size);
        }
        else
        {
            services.AddEbicoServer();
        }

        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerKeyStore>(),
            provider.GetRequiredService<IDownloadDataProvider>(),
            provider.GetRequiredService<IDownloadTransactionStore>());
    }

    private static SubscriberKeyRef KeyRef()
        => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

    private static string DownloadOrderType(EbicsVersion version)
        => version == EbicsVersion.H005 ? "BTD" : "FDL";

    private async Task SeedSubscriberAsync(IMasterDataManager master, SubscriberState state)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);

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
