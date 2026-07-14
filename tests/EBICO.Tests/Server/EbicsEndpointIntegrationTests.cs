extern alias EbicoServer;
using System.Net;
using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Server;
using EBICO.Server.State;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end tests for the EBICS HTTP endpoint of the server host skeleton (issue #25), driven
/// through <see cref="WebApplicationFactory{TEntryPoint}"/>: a well-formed request yields HTTP 200
/// with a well-formed EBICS error response; transport problems yield 4xx.
/// </summary>
public class EbicsEndpointIntegrationTests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public EbicsEndpointIntegrationTests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    [Fact]
    public async Task PostEbics_WellFormedRequestUnknownOrderType_Returns200_WithUnsupportedOrderType()
    {
        var client = _factory.CreateClient();
        var content = new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091006");
    }

    [Fact]
    public async Task PostEbics_MalformedXml_Returns200_WithInvalidXml()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("<not-xml", Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091010");
    }

    [Fact]
    public async Task PostEbics_EmptyBody_Returns200_WithInvalidXml()
    {
        var client = _factory.CreateClient();
        var content = new StringContent(string.Empty, Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("091010");
    }

    [Fact]
    public async Task PostEbics_UnsupportedVersionNamespace_Returns200_WithErrorInFallbackVersion()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("<ebicsRequest xmlns=\"urn:example:unknown\"/>", Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        envelope.ProtocolVersion.Should().Be(EbicsVersion.H005);
        ServerTestHelpers.ReadReturnCodes(envelope).HeaderCode.Should().Be("061002");
    }

    [Fact]
    public async Task PostEbics_Ini_Returns200_StoresKey_AndInitializesSubscriber()
    {
        // Isolated host so seeding this subscriber cannot leak into the other tests' shared factory.
        var factory = _factory.WithWebHostBuilder(_ => { });
        var host = HostId.Create("INIHOST");
        var partner = PartnerId.Create("INIPART");
        var user = UserId.Create("INIUSER");

        var master = factory.Services.GetRequiredService<IMasterDataManager>();
        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);

        var key = RsaKeyMaterial.Generate();
        var xml = ServerTestHelpers.BuildUnsecuredIniRequest(
            EbicsVersion.H004, "INIHOST", "INIPART", "INIUSER", rsaKey: key);
        var response = await factory.CreateClient().PostAsync("/ebics", new StringContent(xml, Encoding.UTF8, "text/xml"), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(await response.Content.ReadAsStringAsync(_ct));
        envelope.GetType().Name.Should().Be("EbicsKeyManagementResponse");
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("000000");

        var subscriber = await master.GetSubscriberAsync(host, partner, user, _ct);
        subscriber!.State.Should().Be(SubscriberState.Initialized);

        var keys = factory.Services.GetRequiredService<IServerKeyStore>();
        (await keys.ContainsAsync(new SubscriberKeyRef(host, partner, user), KeyPurpose.Signature, _ct))
            .Should().BeTrue();
    }

    [Fact]
    public async Task PostEbics_Upload_InitThenTransfer_Returns200_AndStoresOrderData()
    {
        // Isolated host (own in-memory stores) so the seeded subscriber and transaction stay local.
        var factory = _factory.WithWebHostBuilder(_ => { });
        var host = HostId.Create("ULHOST");
        var partner = PartnerId.Create("ULPART");
        var user = UserId.Create("ULUSER");

        var master = factory.Services.GetRequiredService<IMasterDataManager>();
        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);

        var bank = await factory.Services.GetRequiredService<IServerBankKeyStore>().GetOrCreateAsync(host, _ct);
        var orderData = Encoding.UTF8.GetBytes("<order>http upload</order>");
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H004, "ULHOST", "ULPART", "ULUSER", orderData, bank.Encryption, bank.EncryptionVersion);

        var client = factory.CreateClient();

        // Initialisation over HTTP -> 200 with a transaction id.
        var initResponse = await client.PostAsync("/ebics", new StringContent(upload.InitXml, Encoding.UTF8, "text/xml"), _ct);
        initResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initEnvelope = EbicsXmlSerializer.DeserializeEnvelope(await initResponse.Content.ReadAsStringAsync(_ct));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        transactionId.Should().NotBeNull().And.HaveCount(16);

        // Transfer over HTTP -> 200 OK/OK, order data reassembled in the transaction store.
        var transferXml = ServerTestHelpers.BuildUploadTransferRequest(
            EbicsVersion.H004, "ULHOST", transactionId!, segmentNumber: 1, lastSegment: true, segment: upload.Segments[0]);
        var transferResponse = await client.PostAsync("/ebics", new StringContent(transferXml, Encoding.UTF8, "text/xml"), _ct);
        transferResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var transferEnvelope = EbicsXmlSerializer.DeserializeEnvelope(await transferResponse.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(transferEnvelope).Should().Be(("000000", "000000"));

        var store = factory.Services.GetRequiredService<IUploadTransactionStore>();
        store.TryGet(Convert.ToHexString(transactionId!), out var transaction).Should().BeTrue();
        transaction!.OrderData.Should().Equal(orderData);
    }

    [Fact]
    public async Task PostEbics_Download_InitReceipt_Returns200_SeededViaAdminApi_AndConsumesData()
    {
        // Isolated host (own in-memory stores) so the seeded subscriber and data stay local.
        var factory = _factory.WithWebHostBuilder(_ => { });
        var host = HostId.Create("DLHOST");
        var partner = PartnerId.Create("DLPART");
        var user = UserId.Create("DLUSER");

        var master = factory.Services.GetRequiredService<IMasterDataManager>();
        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);

        // The subscriber's encryption key must be on file (normally stored during HIA); keep the private
        // part to decrypt the downloaded data.
        var subscriberEnc = RsaKeyMaterial.Generate();
        await factory.Services.GetRequiredService<IServerKeyStore>().StoreAsync(
            new SubscriberKeyRef(host, partner, user),
            new StoredPublicKey(subscriberEnc.ToPublicOnly(), KeyVersion.Create("E002")),
            _ct);

        var client = factory.CreateClient();

        // Make order data available for download via the admin API (base64 JSON).
        var orderData = Encoding.UTF8.GetBytes("<order>http download</order>");
        var seedJson = $"{{\"base64Data\":\"{Convert.ToBase64String(orderData)}\"}}";
        var seedResponse = await client.PostAsync(
            "/admin/banks/DLHOST/partners/DLPART/subscribers/DLUSER/downloads/FDL",
            new StringContent(seedJson, Encoding.UTF8, "application/json"), _ct);
        seedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Initialisation over HTTP -> 200 with a transaction id and the (single) encrypted segment.
        var initResponse = await client.PostAsync(
            "/ebics", new StringContent(ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H004, "DLHOST", "DLPART", "DLUSER"), Encoding.UTF8, "text/xml"), _ct);
        initResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initEnvelope = EbicsXmlSerializer.DeserializeEnvelope(await initResponse.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(initEnvelope).Should().Be(("000000", "000000"));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        transactionId.Should().NotBeNull().And.HaveCount(16);
        ServerTestHelpers.ReadNumSegments(initEnvelope).Should().Be(1UL);

        // The delivered data decrypts (subscriber private key) and decompresses to the original.
        var (txKey, segment, _, _) = ServerTestHelpers.ReadDownloadDataTransfer(EbicsVersion.H004, initEnvelope);
        var decrypted = EncryptionE002.Decrypt(new EncryptedOrderData(txKey!, segment!), subscriberEnc, KeyVersion.Create("E002"));
        EbicsCompression.Decompress(decrypted).Should().Equal(orderData);

        // Positive receipt over HTTP -> 200 with EBICS_DOWNLOAD_POSTPROCESS_DONE (011000, technical/header).
        var receiptResponse = await client.PostAsync(
            "/ebics", new StringContent(ServerTestHelpers.BuildDownloadReceiptRequest(EbicsVersion.H004, "DLHOST", transactionId!, 0), Encoding.UTF8, "text/xml"), _ct);
        receiptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var receiptEnvelope = EbicsXmlSerializer.DeserializeEnvelope(await receiptResponse.Content.ReadAsStringAsync(_ct));
        ServerTestHelpers.ReadReturnCodes(receiptEnvelope).Should().Be(("011000", "000000"));

        // The data was consumed: the admin queue is empty again.
        var statusResponse = await client.GetStringAsync("/admin/banks/DLHOST/partners/DLPART/subscribers/DLUSER/downloads/FDL", _ct);
        statusResponse.Should().Contain("\"pending\":0");
    }

    [Fact]
    public async Task PostEbics_WrongContentType_Returns415()
    {
        var client = _factory.CreateClient();
        var content = new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task PostEbics_BodyTooLarge_Returns413()
    {
        var client = _factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                s.Configure<EbicoServerOptions>(o => o.MaxRequestBodyBytes = 32)))
            .CreateClient();
        var content = new StringContent(ServerTestHelpers.BuildH004Request("AAA"), Encoding.UTF8, "text/xml");

        var response = await client.PostAsync("/ebics", content, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }
}
