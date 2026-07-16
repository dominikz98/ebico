using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using EBICO.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Server;

/// <summary>
/// End-to-end tests for the distributed electronic signature (EDS / VEU, issue #42): a payment upload
/// submitted for distributed signing is parked (HVU/HVZ list it, HVD/HVT detail it), a second authorised
/// subscriber signs it via HVE, and once the required number of signatures is reached the order is released
/// (its <c>pain.002</c> filed) and removed from the open-order overview. HVS cancels an order. Negative
/// cases cover an unknown order id, an unauthorised signer and a duplicate signature. Driven through the
/// <see cref="EbicsRequestPipeline"/> with requests built from the committed Core bindings.
/// </summary>
public class VeuOrdersTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string Alice = "USER01";   // originator, CCT first signature (A)
    private const string Bob = "USER02";     // co-signer, CCT second signature (B)
    private const string Carol = "USER03";   // transport-only for CCT (cannot authorise)
    private const string StatusReportOrderType = "PSR";

    private static readonly BusinessTransactionFormat CctBtf = new("SCT", messageName: "pain.001");

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public static TheoryData<EbicsVersion> AllVersions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task DistributedSigning_ParkSignRelease_FullFlow(EbicsVersion version)
    {
        var server = BuildServer();
        await SeedAsync(server);
        var bobEnc = await StoreKeyAsync(server, Bob);
        var bank = await server.BankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        // Alice submits a CCT flagged for distributed signing: it is parked, not released.
        var pain = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([100.00m], messageId: "MSG-VEU-1"));
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, Alice, pain, bank.Encryption, bank.EncryptionVersion,
            btf: CctBtf, orderType: version == EbicsVersion.H005 ? null : "CCT", distributedSignature: true);
        ServerTestHelpers.ReadReturnCodes(await RunUploadAsync(server, version, upload)).BodyCode.Should().Be("000000");

        var orders = await server.Veu.ListAsync(HostId.Create(Host), PartnerId.Create(Partner), _ct);
        orders.Should().HaveCount(1);
        var order = orders[0];
        order.NumSigDone.Should().Be(1);           // Alice's first signature (A)
        order.NumSigRequired.Should().Be(2);
        (await server.DownloadData.CountAsync(KeyRef(Alice), StatusReportOrderType, _ct)).Should().Be(0);

        // Bob sees the open order via HVU and its status via HVD.
        var hvu = await DownloadXmlAsync(server, version, Bob, bobEnc, "HVU");
        hvu.Should().Contain("HVUResponseOrderData").And.Contain(order.OrderId).And.Contain(Alice);

        var hvd = await DownloadXmlAsync(server, version, Bob, bobEnc, "HVD", DetailParams(version, "HVD", order.OrderId));
        hvd.Should().Contain("HVDResponseOrderData").And.Contain("DataDigest");

        var hvt = await DownloadXmlAsync(server, version, Bob, bobEnc, "HVT", DetailParams(version, "HVT", order.OrderId));
        hvt.Should().Contain("HVTResponseOrderData");

        // Bob signs via HVE — the second signature reaches the required count and releases the order.
        var hve = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, Bob, Encoding.UTF8.GetBytes("<ES/>"), bank.Encryption, bank.EncryptionVersion,
            orderType: "HVE", orderParams: SignatureParams(version, "HVE", order.OrderId));
        ServerTestHelpers.ReadReturnCodes(await RunUploadAsync(server, version, hve)).BodyCode.Should().Be("000000");

        // Released: gone from the overview, and the pain.002 is filed for the originator (Alice).
        (await server.Veu.ListAsync(HostId.Create(Host), PartnerId.Create(Partner), _ct)).Should().BeEmpty();
        (await server.DownloadData.CountAsync(KeyRef(Alice), StatusReportOrderType, _ct)).Should().Be(1);
    }

    [Fact]
    public async Task Hvu_WithNoOpenOrders_ReturnsEmptyOverview()
    {
        var server = BuildServer();
        await SeedAsync(server);
        var bobEnc = await StoreKeyAsync(server, Bob);

        var hvu = await DownloadXmlAsync(server, EbicsVersion.H005, Bob, bobEnc, "HVU");

        hvu.Should().Contain("HVUResponseOrderData");
        hvu.Should().NotContain("OrderID");
    }

    [Fact]
    public async Task Hve_UnknownOrderId_Returns091121()
    {
        var server = BuildServer();
        await SeedAsync(server);
        var bank = await server.BankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var hve = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, Bob, Encoding.UTF8.GetBytes("<ES/>"), bank.Encryption, bank.EncryptionVersion,
            orderType: "HVE", orderParams: SignatureParams(EbicsVersion.H005, "HVE", "ZZZZ"));

        ServerTestHelpers.ReadReturnCodes(await RunUploadAsync(server, EbicsVersion.H005, hve)).BodyCode.Should().Be("091121");
    }

    [Fact]
    public async Task Hve_ByUnauthorisedSigner_Returns090003()
    {
        var server = BuildServer();
        await SeedAsync(server);
        var order = await ParkOrderAsync(server);

        // Carol holds only a transport permission for CCT — she may submit an HVE but cannot authorise it.
        var bank = await server.BankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        var hve = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, Carol, Encoding.UTF8.GetBytes("<ES/>"), bank.Encryption, bank.EncryptionVersion,
            orderType: "HVE", orderParams: SignatureParams(EbicsVersion.H005, "HVE", order.OrderId));

        ServerTestHelpers.ReadReturnCodes(await RunUploadAsync(server, EbicsVersion.H005, hve)).BodyCode.Should().Be("090003");
    }

    [Fact]
    public async Task Hve_ByTheSameSignerTwice_IsRejected()
    {
        var server = BuildServer();
        await SeedAsync(server);
        var order = await ParkOrderAsync(server);

        // Alice already signed at submission (first signature); signing again is a duplicate.
        var bank = await server.BankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        var hve = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, Alice, Encoding.UTF8.GetBytes("<ES/>"), bank.Encryption, bank.EncryptionVersion,
            orderType: "HVE", orderParams: SignatureParams(EbicsVersion.H005, "HVE", order.OrderId));

        ServerTestHelpers.ReadReturnCodes(await RunUploadAsync(server, EbicsVersion.H005, hve)).BodyCode.Should().Be("090004");
        (await server.Veu.ListAsync(HostId.Create(Host), PartnerId.Create(Partner), _ct)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Hvs_ByOriginator_CancelsTheOrder()
    {
        var server = BuildServer();
        await SeedAsync(server);
        var order = await ParkOrderAsync(server);

        var bank = await server.BankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        var hvs = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, Alice, Encoding.UTF8.GetBytes("<HVSRequestOrderData/>"), bank.Encryption, bank.EncryptionVersion,
            orderType: "HVS", orderParams: SignatureParams(EbicsVersion.H005, "HVS", order.OrderId));

        ServerTestHelpers.ReadReturnCodes(await RunUploadAsync(server, EbicsVersion.H005, hvs)).BodyCode.Should().Be("000000");
        (await server.Veu.ListAsync(HostId.Create(Host), PartnerId.Create(Partner), _ct)).Should().BeEmpty();
        // A cancelled order is never released, so no status report is filed.
        (await server.DownloadData.CountAsync(KeyRef(Alice), StatusReportOrderType, _ct)).Should().Be(0);
    }

    // --- Helpers ---------------------------------------------------------------------------

    private sealed record Server(
        IEbicsRequestPipeline Pipeline,
        IMasterDataManager Master,
        IServerBankKeyStore BankKeys,
        IServerKeyStore Keys,
        IDownloadDataProvider DownloadData,
        IOpenVeuStore Veu);

    private static Server BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return new Server(
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerBankKeyStore>(),
            provider.GetRequiredService<IServerKeyStore>(),
            provider.GetRequiredService<IDownloadDataProvider>(),
            provider.GetRequiredService<IOpenVeuStore>());
    }

    // Parks one CCT order (submitted by Alice, flagged for distributed signing) and returns it.
    private async Task<OpenVeuOrder> ParkOrderAsync(Server server)
    {
        var bank = await server.BankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        var pain = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([100.00m], messageId: "MSG-VEU-PARK"));
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, Alice, pain, bank.Encryption, bank.EncryptionVersion,
            btf: CctBtf, distributedSignature: true);
        ServerTestHelpers.ReadReturnCodes(await RunUploadAsync(server, EbicsVersion.H005, upload)).BodyCode.Should().Be("000000");

        var orders = await server.Veu.ListAsync(HostId.Create(Host), PartnerId.Create(Partner), _ct);
        orders.Should().HaveCount(1);
        return orders[0];
    }

    private async Task<IEbicsEnvelope> RunUploadAsync(Server server, EbicsVersion version, ServerTestHelpers.UploadRequest upload)
    {
        var initEnvelope = Deserialize(await server.Pipeline.ProcessAsync(upload.InitXml, _ct));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        transactionId.Should().NotBeNull();

        IEbicsEnvelope last = initEnvelope;
        for (var i = 0; i < upload.Segments.Count; i++)
        {
            var transfer = ServerTestHelpers.BuildUploadTransferRequest(
                version, Host, transactionId!, (ulong)(i + 1), i == upload.Segments.Count - 1, upload.Segments[i]);
            last = Deserialize(await server.Pipeline.ProcessAsync(transfer, _ct));
        }

        return last;
    }

    private async Task<string> DownloadXmlAsync(
        Server server, EbicsVersion version, string userId, RsaKeyMaterial enc, string orderType, object? orderParams = null)
    {
        var init = Deserialize(await server.Pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, userId, orderType: orderType, orderParams: orderParams), _ct));
        ServerTestHelpers.ReadReturnCodes(init).Should().Be(("000000", "000000"));

        var (txKey, segment, _, _) = ServerTestHelpers.ReadDownloadDataTransfer(version, init);
        var decrypted = EncryptionE002.Decrypt(new EncryptedOrderData(txKey!, segment!), enc, KeyVersion.Create("E002"));
        return Encoding.UTF8.GetString(EbicsCompression.Decompress(decrypted));
    }

    private static SubscriberKeyRef KeyRef(string userId)
        => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(userId));

    private async Task SeedAsync(Server server)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);

        await server.Master.SaveBankAsync(new Bank(host, "EBICO Test Bank", url: "https://ebico.example/ebics"), _ct);
        await server.Master.SavePartnerAsync(new Partner(host, partner), _ct);
        await server.BankKeys.GetOrCreateAsync(host, _ct);

        // Alice: originator with a first (A) signature for CCT; Bob: second (B); both may read/sign VEU orders.
        await SaveSubscriberAsync(server, Alice, "Alice",
            new SubscriberPermission("CCT", SignatureClass.A),
            new SubscriberPermission("HVU", SignatureClass.T),
            new SubscriberPermission("HVZ", SignatureClass.T),
            new SubscriberPermission("HVD", SignatureClass.T),
            new SubscriberPermission("HVT", SignatureClass.T),
            new SubscriberPermission("HVE", SignatureClass.A),
            new SubscriberPermission("HVS", SignatureClass.A));
        await SaveSubscriberAsync(server, Bob, "Bob",
            new SubscriberPermission("CCT", SignatureClass.B),
            new SubscriberPermission("HVU", SignatureClass.T),
            new SubscriberPermission("HVZ", SignatureClass.T),
            new SubscriberPermission("HVD", SignatureClass.T),
            new SubscriberPermission("HVT", SignatureClass.T),
            new SubscriberPermission("HVE", SignatureClass.B),
            new SubscriberPermission("HVS", SignatureClass.B));
        // Carol may submit an HVE (transport) but cannot authorise CCT.
        await SaveSubscriberAsync(server, Carol, "Carol",
            new SubscriberPermission("CCT", SignatureClass.T),
            new SubscriberPermission("HVE", SignatureClass.T));
    }

    private async Task SaveSubscriberAsync(Server server, string userId, string name, params SubscriberPermission[] permissions)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(userId);

        await server.Master.SaveSubscriberAsync(new Subscriber(host, partner, user, permissions: permissions, name: name), _ct);
        await server.Master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await server.Master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
    }

    private async Task<RsaKeyMaterial> StoreKeyAsync(Server server, string userId)
    {
        var enc = RsaKeyMaterial.Generate();
        await server.Keys.StoreAsync(KeyRef(userId), new StoredPublicKey(enc.ToPublicOnly(), KeyVersion.Create("E002")), _ct);
        return enc;
    }

    // Version-specific HVE/HVS order params carrying the referenced order id.
    private static object SignatureParams(EbicsVersion version, string orderType, string orderId) => (version, orderType) switch
    {
        (EbicsVersion.H003, "HVS") => new H003.HvsOrderParamsType { OrderId = orderId },
        (EbicsVersion.H004, "HVS") => new H004.HvsOrderParamsType { OrderId = orderId },
        (EbicsVersion.H005, "HVS") => new H005.HvsOrderParamsType { OrderId = orderId },
        (EbicsVersion.H003, _) => new H003.HveOrderParamsType { OrderId = orderId },
        (EbicsVersion.H004, _) => new H004.HveOrderParamsType { OrderId = orderId },
        _ => new H005.HveOrderParamsType { OrderId = orderId },
    };

    // Version-specific HVD/HVT order params carrying the referenced order id.
    private static object DetailParams(EbicsVersion version, string orderType, string orderId) => (version, orderType) switch
    {
        (EbicsVersion.H003, "HVT") => new H003.HvtOrderParamsType { OrderId = orderId },
        (EbicsVersion.H004, "HVT") => new H004.HvtOrderParamsType { OrderId = orderId },
        (EbicsVersion.H005, "HVT") => new H005.HvtOrderParamsType { OrderId = orderId },
        (EbicsVersion.H003, _) => new H003.HvdOrderParamsType { OrderId = orderId },
        (EbicsVersion.H004, _) => new H004.HvdOrderParamsType { OrderId = orderId },
        _ => new H005.HvdOrderParamsType { OrderId = orderId },
    };

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
