using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// End-to-end tests for statement/report download orders (issue #40): a download initialisation for a
/// statement order type (STA/VMK/C53/C52/C54) is routed to the download engine, resolved to its classical
/// code, generated on demand (synthetic MT940/MT942/camt.05x wrapped in a ZIP) when nothing is pre-seeded,
/// compressed/E002-encrypted/segmented and delivered. Driven through the pipeline; the delivered payload is
/// decrypted with the subscriber's private key, decompressed and unzipped to assert the generated content.
/// </summary>
public class StatementDownloadTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private static readonly BusinessTransactionFormat Camt053Btf = new("EOP", messageName: "camt.053");

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Download_H005Btd_Camt053_GeneratesAndDelivers_ThenReceiptConsumes()
    {
        var (pipeline, master, keys, _, store) = BuildServer();
        var enc = await SeedReadyAsync(master, keys, "C53");

        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H005, Host, Partner, User, btf: Camt053Btf), _ct));
        ServerTestHelpers.ReadReturnCodes(init).Should().Be(("000000", "000000"));
        var transactionId = ServerTestHelpers.ReadTransactionId(init);

        var xml = Encoding.UTF8.GetString(Unzip(DecryptDecompress(EbicsVersion.H005, init, enc)));
        xml.Should().Contain("camt.053.001.08").And.Contain("BkToCstmrStmt");

        var receipt = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadReceiptRequest(EbicsVersion.H005, Host, transactionId!, 0), _ct));
        ServerTestHelpers.ReadReturnCodes(receipt).Should().Be(("011000", "000000"));
        store.Count.Should().Be(0);
    }

    [Fact]
    public async Task Download_H004Fdl_WithCamt053FileFormat_GeneratesC53()
    {
        var (pipeline, master, keys, _, _) = BuildServer();
        var enc = await SeedReadyAsync(master, keys, "C53");

        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H004, Host, Partner, User, fileFormat: "camt.053"), _ct));
        ServerTestHelpers.ReadReturnCodes(init).Should().Be(("000000", "000000"));

        Encoding.UTF8.GetString(Unzip(DecryptDecompress(EbicsVersion.H004, init, enc)))
            .Should().Contain("camt.053.001.08");
    }

    [Fact]
    public async Task Download_H004_DirectStaOrderType_GeneratesMt940()
    {
        var (pipeline, master, keys, _, _) = BuildServer();
        var enc = await SeedReadyAsync(master, keys, "STA");

        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H004, Host, Partner, User, orderType: "STA"), _ct));
        ServerTestHelpers.ReadReturnCodes(init).Should().Be(("000000", "000000"));

        var text = Encoding.UTF8.GetString(Unzip(DecryptDecompress(EbicsVersion.H004, init, enc)));
        text.Should().Contain(":20:").And.Contain(":60F:");
    }

    [Fact]
    public async Task Download_WithDateRange_LimitsBookingDatesToRange()
    {
        var (pipeline, master, keys, _, _) = BuildServer();
        var enc = await SeedReadyAsync(master, keys, "C53");
        var range = new DateRange(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 12));

        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H005, Host, Partner, User, btf: Camt053Btf, dateRange: range), _ct));

        XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.08";
        var doc = XDocument.Parse(Encoding.UTF8.GetString(Unzip(DecryptDecompress(EbicsVersion.H005, init, enc))));
        doc.ToString().Should().Contain("2026-03-10").And.Contain("2026-03-12");

        var bookingDates = doc.Descendants(ns + "Ntry")
            .Select(n => DateOnly.ParseExact(n.Element(ns + "BookgDt")!.Element(ns + "Dt")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        bookingDates.Should().OnlyContain(d => d >= range.Start!.Value && d <= range.End!.Value);
    }

    [Fact]
    public async Task Download_PreSeededData_WinsOverGeneration()
    {
        var (pipeline, master, keys, provider, _) = BuildServer();
        var enc = await SeedReadyAsync(master, keys, "C53");
        var seeded = Encoding.UTF8.GetBytes("<seeded>raw payload</seeded>");
        await provider.EnqueueAsync(KeyRef(), "C53", seeded, _ct);

        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H005, Host, Partner, User, btf: Camt053Btf), _ct));

        // The delivered plaintext is the seeded payload verbatim (not a generated, ZIP-wrapped statement).
        DecryptDecompress(EbicsVersion.H005, init, enc).Should().Equal(seeded);
    }

    [Fact]
    public async Task Download_WithoutPermissionForResolvedType_Returns090003()
    {
        var (pipeline, master, keys, _, _) = BuildServer();
        await SeedReadyAsync(master, keys, "STA"); // STA granted, C53 not

        var init = Deserialize(await pipeline.ProcessAsync(
            ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H005, Host, Partner, User, btf: Camt053Btf), _ct));

        ServerTestHelpers.ReadReturnCodes(init).BodyCode.Should().Be("090003");
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerKeyStore Keys, IDownloadDataProvider Provider, IDownloadTransactionStore Store) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerKeyStore>(),
            provider.GetRequiredService<IDownloadDataProvider>(),
            provider.GetRequiredService<IDownloadTransactionStore>());
    }

    private static SubscriberKeyRef KeyRef()
        => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

    private async Task<RsaKeyMaterial> SeedReadyAsync(IMasterDataManager master, IServerKeyStore keys, params string[] orderTypes)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(
            new Subscriber(host, partner, user, permissions: orderTypes.Select(o => new SubscriberPermission(o, SignatureClass.T)).ToArray()),
            _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);

        var enc = RsaKeyMaterial.Generate();
        await keys.StoreAsync(KeyRef(), new StoredPublicKey(enc.ToPublicOnly(), KeyVersion.Create("E002")), _ct);
        return enc;
    }

    // Decrypts segment 1 with the subscriber's private key and decompresses it to the plaintext the engine
    // compressed (for a generated statement that plaintext is the ZIP container).
    private static byte[] DecryptDecompress(EbicsVersion version, IEbicsEnvelope initEnvelope, RsaKeyMaterial subscriberEnc)
    {
        var (txKey, segment, _, _) = ServerTestHelpers.ReadDownloadDataTransfer(version, initEnvelope);
        var decrypted = EncryptionE002.Decrypt(new EncryptedOrderData(txKey!, segment!), subscriberEnc, KeyVersion.Create("E002"));
        return EbicsCompression.Decompress(decrypted);
    }

    private static byte[] Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var stream = archive.Entries.Single().Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
