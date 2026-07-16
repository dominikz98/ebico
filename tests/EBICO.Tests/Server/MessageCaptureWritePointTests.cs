using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using EBICO.Server.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests that the raw-message capture write point (issue #54) records the request/response XML of a
/// transaction end-to-end: the upload initialisation and transfer messages are captured (keyed by
/// transaction id, with their phase), while a key-management order without a transaction id is not.
/// Driven through the real pipeline over a DI provider, then asserted by reading the resolved
/// <see cref="IMessageCaptureStore"/>.
/// </summary>
public class MessageCaptureWritePointTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_CapturesInitAndTransferRawXml()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IEbicsRequestPipeline>();
        var master = provider.GetRequiredService<IMasterDataManager>();
        var bankKeys = provider.GetRequiredService<IServerBankKeyStore>();
        var captures = provider.GetRequiredService<IMessageCaptureStore>();

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
        var messages = await captures.GetAsync(hexId, _ct);

        messages.Should().HaveCount(2);
        messages.Select(m => m.Phase).Should().Equal([EbicsTransactionPhase.Initialisation, EbicsTransactionPhase.Transfer]);
        messages.Should().OnlyContain(m => !string.IsNullOrEmpty(m.RequestXml) && !string.IsNullOrEmpty(m.ResponseXml));
        messages[0].RequestXml.Should().Contain("ebicsRequest");
        messages[0].ResponseXml.Should().Contain("ebicsResponse");
        messages[0].OrderType.Should().Be("FUL");
    }

    [Fact]
    public async Task KeyManagementIni_ProducesNoCapture()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IEbicsRequestPipeline>();
        var log = provider.GetRequiredService<IEventLog>();
        var captures = provider.GetRequiredService<IMessageCaptureStore>();

        var ini = ServerTestHelpers.BuildUnsecuredIniRequest(
            EbicsVersion.H004, Host, Partner, User, rsaKey: RsaKeyMaterial.Generate());
        await pipeline.ProcessAsync(ini, _ct);

        // The INI produced an event (central write point) but it carries no transaction id, so nothing is
        // captured. Sum the captures across every transaction id the log knows to prove the store is empty.
        var events = await log.QueryAsync(new EbicsEventQuery(), _ct);
        events.Should().Contain(e => e.Type == EbicsEventType.RequestReceived);

        var transactionIds = events.Select(e => e.TransactionId).Where(t => t is not null).Distinct().ToList();
        var totalCaptures = 0;
        foreach (var id in transactionIds)
        {
            totalCaptures += (await captures.GetAsync(id!, _ct)).Count;
        }

        totalCaptures.Should().Be(0);
    }

    private async Task SeedReadySubscriberAsync(IMasterDataManager master)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(
            new Subscriber(host, partner, user, permissions:
            [
                new SubscriberPermission("FUL", SignatureClass.T),
                new SubscriberPermission("BTU", SignatureClass.T),
            ]),
            _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
