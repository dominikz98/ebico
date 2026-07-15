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
/// End-to-end tests for the status/protocol download orders (issue #41): HTD/HKD/HAA/HPD are generated from
/// the seeded master data, HAC/PTK are projected from the customer-visible event log. Each is routed to the
/// download engine, authorised by the subscriber's permission, generated on demand, E002-encrypted and
/// delivered; the tests decrypt the payload and assert the produced content.
/// </summary>
public class StatusProtocolDownloadTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private static readonly string[] AdminOrders = ["HTD", "HKD", "HAA", "HPD", "HAC", "PTK", "C53"];

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public async Task Htd_GeneratesSubscriberData_AcrossVersions(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        var enc = await SeedReadyAsync(master, keys);

        var xml = await DownloadTextAsync(pipeline, version, "HTD", enc);

        xml.Should().Contain("HTDResponseOrderData");
        xml.Should().Contain(Host).And.Contain(User);
        xml.Should().Contain("Acme GmbH").And.Contain("DE89370400440532013000");
        xml.Should().Contain("Alice");
    }

    [Fact]
    public async Task Hkd_ListsAllSubscribersOfCustomer()
    {
        var (pipeline, master, keys) = BuildServer();
        var enc = await SeedReadyAsync(master, keys);
        await master.SaveSubscriberAsync(
            new Subscriber(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create("USER02"),
                permissions: [new SubscriberPermission("C53", SignatureClass.T)], name: "Bob"),
            _ct);

        var xml = await DownloadTextAsync(pipeline, EbicsVersion.H005, "HKD", enc);

        xml.Should().Contain("HKDResponseOrderData");
        xml.Should().Contain(User).And.Contain("USER02");
        xml.Should().Contain("Alice").And.Contain("Bob");
    }

    [Fact]
    public async Task Haa_ListsDownloadableOrderTypes()
    {
        var (pipeline, master, keys) = BuildServer();
        var enc = await SeedReadyAsync(master, keys);

        // H005 lists BTF services (camt.053 for the C53 permission).
        var h005 = await DownloadTextAsync(pipeline, EbicsVersion.H005, "HAA", enc);
        h005.Should().Contain("HAAResponseOrderData").And.Contain("camt.053");

        // H004 lists the classical order-type codes.
        var h004 = await DownloadTextAsync(pipeline, EbicsVersion.H004, "HAA", enc);
        h004.Should().Contain("HAAResponseOrderData").And.Contain("C53");
    }

    [Fact]
    public async Task Hpd_ReturnsBankAccessAndProtocolParameters()
    {
        var (pipeline, master, keys) = BuildServer();
        var enc = await SeedReadyAsync(master, keys);

        var xml = await DownloadTextAsync(pipeline, EbicsVersion.H005, "HPD", enc);

        xml.Should().Contain("HPDResponseOrderData");
        xml.Should().Contain(Host).And.Contain("https://ebico.example/ebics");
        xml.Should().Contain("H005").And.Contain("E002");
    }

    [Fact]
    public async Task Hac_ProjectsCustomerVisibleEvents()
    {
        var (pipeline, master, keys) = BuildServer();
        var enc = await SeedReadyAsync(master, keys);

        // A prior download produces customer-visible events (DownloadStarted / OrderAccepted).
        await DownloadTextAsync(pipeline, EbicsVersion.H005, "HTD", enc);

        var xml = await DownloadTextAsync(pipeline, EbicsVersion.H005, "HAC", enc);

        xml.Should().Contain("HACResponseOrderData").And.Contain("ProtocolEntry");
        xml.Should().Contain("HTD");
    }

    [Fact]
    public async Task Ptk_ProjectsCustomerVisibleEventsAsText()
    {
        var (pipeline, master, keys) = BuildServer();
        var enc = await SeedReadyAsync(master, keys);
        await DownloadTextAsync(pipeline, EbicsVersion.H004, "HTD", enc);

        var text = await DownloadTextAsync(pipeline, EbicsVersion.H004, "PTK", enc);

        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("HTD");
    }

    [Fact]
    public async Task WithoutPermission_Returns090003()
    {
        var (pipeline, master, keys) = BuildServer();
        // Ready subscriber holds only C53 — HTD is not authorised.
        var enc = await SeedReadyAsync(master, keys, "C53");

        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H005, Host, Partner, User, orderType: "HTD"), _ct));

        ServerTestHelpers.ReadReturnCodes(init).BodyCode.Should().Be("090003");
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerKeyStore Keys) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerKeyStore>());
    }

    private static SubscriberKeyRef KeyRef()
        => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

    private async Task<RsaKeyMaterial> SeedReadyAsync(IMasterDataManager master, IServerKeyStore keys, params string[] permissions)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);
        var orderTypes = permissions.Length == 0 ? AdminOrders : permissions;

        await master.SaveBankAsync(new Bank(host, "EBICO Test Bank", url: "https://ebico.example/ebics"), _ct);
        await master.SavePartnerAsync(
            new Partner(
                host,
                partner,
                "Acme GmbH",
                new Address("Acme GmbH", "Hauptstr. 1", "10115", "Berlin", "BE", "DE"),
                [new BankAccount("DE89370400440532013000", "COBADEFFXXX", "Acme GmbH", "EUR", "Main account", "ACC1")]),
            _ct);
        await master.SaveSubscriberAsync(
            new Subscriber(host, partner, user,
                permissions: orderTypes.Select(o => new SubscriberPermission(o, SignatureClass.T)).ToArray(),
                name: "Alice"),
            _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);

        var enc = RsaKeyMaterial.Generate();
        await keys.StoreAsync(KeyRef(), new StoredPublicKey(enc.ToPublicOnly(), KeyVersion.Create("E002")), _ct);
        return enc;
    }

    // Runs a full download initialisation for an admin order type and returns the delivered plaintext as text.
    private async Task<string> DownloadTextAsync(IEbicsRequestPipeline pipeline, EbicsVersion version, string orderType, RsaKeyMaterial enc)
    {
        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(version, Host, Partner, User, orderType: orderType), _ct));
        ServerTestHelpers.ReadReturnCodes(init).Should().Be(("000000", "000000"));

        var (txKey, segment, _, _) = ServerTestHelpers.ReadDownloadDataTransfer(version, init);
        var decrypted = EncryptionE002.Decrypt(new EncryptedOrderData(txKey!, segment!), enc, KeyVersion.Create("E002"));
        return Encoding.UTF8.GetString(EbicsCompression.Decompress(decrypted));
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
