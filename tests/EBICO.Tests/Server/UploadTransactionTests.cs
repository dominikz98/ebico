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
/// Tests for the upload transaction engine (issue #32): the two-phase EBICS upload
/// (Initialisation + Transfer). The initialisation assigns a transaction id and captures the
/// transaction key; the transfer phase buffers the E002-encrypted order-data segments and, on the
/// last one, reassembles, decrypts and decompresses the order data. Driven end-to-end through
/// <see cref="EbicsRequestPipeline"/>; requests are built from the committed Core bindings (no
/// proprietary fixtures). The order signature (ES) is retained but not verified in this issue.
/// </summary>
public class UploadTransactionTests
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
    public async Task Upload_SingleSegment_CompletesAndStoresOrderData(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, store) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var orderData = Encoding.UTF8.GetBytes("<order>hello ebics upload</order>");
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion);
        upload.Segments.Should().HaveCount(1);

        // Initialisation: assigns the transaction id, echoes phase=Initialisation, OK/OK.
        var initEnvelope = Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct));
        ServerTestHelpers.ReadReturnCodes(initEnvelope).Should().Be(("000000", "000000"));
        ServerTestHelpers.ReadTransactionPhase(initEnvelope).Should().Be("Initialisation");
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        transactionId.Should().NotBeNull().And.HaveCount(16);

        // Transfer: single last segment completes the transaction, echoes phase=Transfer, OK/OK.
        var transferXml = ServerTestHelpers.BuildUploadTransferRequest(
            version, Host, transactionId!, segmentNumber: 1, lastSegment: true, segment: upload.Segments[0]);
        var transferEnvelope = Deserialize(await pipeline.ProcessAsync(transferXml, _ct));
        ServerTestHelpers.ReadReturnCodes(transferEnvelope).Should().Be(("000000", "000000"));
        // Closes the host.md caveat: the response really serializes phase=Transfer (not Initialisation).
        ServerTestHelpers.ReadTransactionPhase(transferEnvelope).Should().Be("Transfer");
        ServerTestHelpers.ReadTransactionId(transferEnvelope).Should().Equal(transactionId);

        // The reassembled, decrypted and decompressed order data matches the original.
        store.TryGet(Convert.ToHexString(transactionId!), out var transaction).Should().BeTrue();
        transaction!.IsComplete.Should().BeTrue();
        transaction.OrderData.Should().Equal(orderData);
    }

    // --- Happy path: multiple segments -----------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Upload_MultipleSegments_ReassemblesOrderData(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, store) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        // Poorly compressible payload + a small segment size forces several segments.
        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion, segmentSizeBytes: 32);
        upload.Segments.Count.Should().BeGreaterThan(1);

        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));
        transactionId.Should().NotBeNull().And.HaveCount(16);

        for (var i = 0; i < upload.Segments.Count; i++)
        {
            var last = i == upload.Segments.Count - 1;
            var transferXml = ServerTestHelpers.BuildUploadTransferRequest(
                version, Host, transactionId!, (ulong)(i + 1), last, upload.Segments[i]);
            ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(transferXml, _ct)))
                .Should().Be(("000000", "000000"));
        }

        store.TryGet(Convert.ToHexString(transactionId!), out var transaction).Should().BeTrue();
        transaction!.OrderData.Should().Equal(orderData);
    }

    // --- Negative: unknown transaction id (091101) -----------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_WithUnknownTransactionId_ReturnsTxUnknownTxid(EbicsVersion version)
    {
        var (pipeline, _, _, _) = BuildServer();
        var unknownTxId = Enumerable.Repeat((byte)0xAB, 16).ToArray();
        var transferXml = ServerTestHelpers.BuildUploadTransferRequest(
            version, Host, unknownTxId, segmentNumber: 1, lastSegment: true, segment: [1, 2, 3, 4]);

        var result = await pipeline.ProcessAsync(transferXml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091101");
    }

    // --- Negative: segment number beyond NumSegments (091104) ------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_SegmentNumberBeyondNumSegments_ReturnsTxSegmentNumberExceeded(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, Encoding.UTF8.GetBytes("data"), bank.Encryption, bank.EncryptionVersion);
        upload.Segments.Should().HaveCount(1); // NumSegments == 1
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        // Segment 2 does not exist for a one-segment transaction.
        var transferXml = ServerTestHelpers.BuildUploadTransferRequest(
            version, Host, transactionId!, segmentNumber: 2, lastSegment: true, segment: upload.Segments[0]);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(transferXml, _ct)))
            .BodyCode.Should().Be("091104");
    }

    // --- Negative: duplicate segment (091103) ----------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_DuplicateSegmentNumber_ReturnsTxMessageReplay(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion, segmentSizeBytes: 32);
        upload.Segments.Count.Should().BeGreaterThan(1);
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        var firstSegment = ServerTestHelpers.BuildUploadTransferRequest(version, Host, transactionId!, 1, false, upload.Segments[0]);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(firstSegment, _ct))).Should().Be(("000000", "000000"));

        // Replaying segment 1 is rejected.
        var replay = ServerTestHelpers.BuildUploadTransferRequest(version, Host, transactionId!, 1, false, upload.Segments[0]);
        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(replay, _ct)))
            .BodyCode.Should().Be("091103");
    }

    // --- Negative: last segment before all received (011101, technical) --------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_LastSegmentBeforeAllReceived_ReturnsTxSegmentNumberUnderrun(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var orderData = Enumerable.Range(0, 500).Select(i => (byte)(i * 131 + 7)).ToArray();
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, orderData, bank.Encryption, bank.EncryptionVersion, segmentSizeBytes: 32);
        upload.Segments.Count.Should().BeGreaterThan(1);
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        // Segment 1 flagged as the last one, but the transaction announced more than one segment.
        var underrun = ServerTestHelpers.BuildUploadTransferRequest(version, Host, transactionId!, 1, lastSegment: true, upload.Segments[0]);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(underrun, _ct)))
            .HeaderCode.Should().Be("011101");
    }

    // --- Negative: subscriber not ready / unknown (091002) ---------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Init_WhenSubscriberNotReady_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, store) = BuildServer();
        // INI done but not HIA -> still Initialized; an upload is not yet allowed.
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, Encoding.UTF8.GetBytes("data"), bank.Encryption, bank.EncryptionVersion);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)))
            .BodyCode.Should().Be("091002");
        store.Count.Should().Be(0); // no transaction was created
    }

    // --- Negative: undecryptable/undecompressable order data (090004) ----------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Transfer_WithUndecryptableOrderData_ReturnsInvalidOrderDataFormat(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        // A valid initialisation (real transaction key), but the transferred segment is not the real
        // ciphertext -> AES decryption/decompression fails on the last segment.
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, Encoding.UTF8.GetBytes("data"), bank.Encryption, bank.EncryptionVersion);
        var transactionId = ServerTestHelpers.ReadTransactionId(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)));

        var garbage = ServerTestHelpers.BuildUploadTransferRequest(version, Host, transactionId!, 1, true, [1, 2, 3, 4, 5]);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(garbage, _ct)))
            .BodyCode.Should().Be("090004");
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerBankKeyStore BankKeys, IUploadTransactionStore Store) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerBankKeyStore>(),
            provider.GetRequiredService<IUploadTransactionStore>());
    }

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

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
