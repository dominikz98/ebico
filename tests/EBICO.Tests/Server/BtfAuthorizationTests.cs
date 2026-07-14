using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Schema.H005;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for the per-BTF order authorisation enforced by the transaction engines (issue #38): a Ready
/// subscriber must hold a permission for the requested order type, otherwise the initialisation is
/// rejected with <c>EBICS_AUTHORISATION_ORDER_TYPE_FAILED</c> (090003). For H005 the BTF service carried
/// in <c>BTUOrderParams</c>/<c>BTDOrderParams</c> is resolved to its classical order-type code before the
/// check; for H003/H004 and for an H005 request without a BTF the raw order/admin-order type is used.
/// </summary>
public class BtfAuthorizationTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";
    private const string AuthorisationFailed = "090003";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    // SCT/pain.001 maps to the classical CCT order type; EOP/camt.053 maps to C53.
    private static readonly BusinessTransactionFormat CreditTransferBtf = new("SCT", messageName: "pain.001");
    private static readonly BusinessTransactionFormat StatementBtf = new("EOP", container: ContainerStringType.Zip, messageName: "camt.053");

    // --- Upload (BTU / FUL) ----------------------------------------------------------------

    [Fact]
    public async Task Upload_H005_WithMappedBtfAndMatchingPermission_ReturnsOk()
    {
        var (provider, pipeline, master) = BuildServer();
        var bankKeys = provider.GetRequiredService<IServerBankKeyStore>();
        await SeedReadyAsync(master, new SubscriberPermission("CCT", SignatureClass.T));
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, User, Encoding.UTF8.GetBytes("<order/>"),
            bank.Encryption, bank.EncryptionVersion, btf: CreditTransferBtf);

        var envelope = Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct));

        ServerTestHelpers.ReadReturnCodes(envelope).Should().Be(("000000", "000000"));
        ServerTestHelpers.ReadTransactionId(envelope).Should().NotBeNull().And.HaveCount(16);
    }

    [Fact]
    public async Task Upload_H005_WithMappedBtfButNoMatchingPermission_ReturnsAuthorisationFailed()
    {
        var (provider, pipeline, master) = BuildServer();
        var bankKeys = provider.GetRequiredService<IServerBankKeyStore>();
        // Permitted for the generic BTU admin type and STA, but not for the resolved CCT.
        await SeedReadyAsync(master, new SubscriberPermission("BTU", SignatureClass.T), new SubscriberPermission("STA", SignatureClass.T));
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, User, Encoding.UTF8.GetBytes("<order/>"),
            bank.Encryption, bank.EncryptionVersion, btf: CreditTransferBtf);

        var envelope = Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct));

        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be(AuthorisationFailed);
        ServerTestHelpers.ReadTransactionId(envelope).Should().BeNull();
    }

    [Fact]
    public async Task Upload_H004Ful_WithoutFulPermission_ReturnsAuthorisationFailed()
    {
        var (provider, pipeline, master) = BuildServer();
        var bankKeys = provider.GetRequiredService<IServerBankKeyStore>();
        await SeedReadyAsync(master, new SubscriberPermission("CCT", SignatureClass.T)); // no FUL permission
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H004, Host, Partner, User, Encoding.UTF8.GetBytes("<order/>"), bank.Encryption, bank.EncryptionVersion);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)))
            .BodyCode.Should().Be(AuthorisationFailed);
    }

    [Fact]
    public async Task Upload_H005WithoutBtf_FallsBackToBtuPermission()
    {
        // No BTF service -> the effective order type falls back to the admin order type "BTU".
        var (provider, pipeline, master) = BuildServer();
        var bankKeys = provider.GetRequiredService<IServerBankKeyStore>();
        await SeedReadyAsync(master, new SubscriberPermission("BTU", SignatureClass.T));
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, User, Encoding.UTF8.GetBytes("<order/>"), bank.Encryption, bank.EncryptionVersion);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct)))
            .Should().Be(("000000", "000000"));
    }

    // --- Download (BTD / FDL) --------------------------------------------------------------

    [Fact]
    public async Task Download_H005_WithMappedBtfButNoMatchingPermission_ReturnsAuthorisationFailed()
    {
        // Authorisation is checked before the encryption key / data are needed, so neither is seeded.
        var (_, pipeline, master) = BuildServer();
        await SeedReadyAsync(master, new SubscriberPermission("BTD", SignatureClass.T)); // not the resolved C53
        var request = ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H005, Host, Partner, User, btf: StatementBtf);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(request, _ct)))
            .BodyCode.Should().Be(AuthorisationFailed);
    }

    [Fact]
    public async Task Download_H005_WithMappedBtfAndMatchingPermission_ReturnsOk()
    {
        var (provider, pipeline, master) = BuildServer();
        var keys = provider.GetRequiredService<IServerKeyStore>();
        var data = provider.GetRequiredService<IDownloadDataProvider>();
        var subscriberEnc = RsaKeyMaterial.Generate();

        await SeedReadyAsync(master, new SubscriberPermission("C53", SignatureClass.T));
        await keys.StoreAsync(KeyRef(), new StoredPublicKey(subscriberEnc.ToPublicOnly(), KeyVersion.Create("E002")), _ct);
        // Order data is provisioned under the admin order type (BTD) the engine dequeues by.
        await data.EnqueueAsync(KeyRef(), "BTD", Encoding.UTF8.GetBytes("<statement/>"), _ct);

        var request = ServerTestHelpers.BuildDownloadInitRequest(EbicsVersion.H005, Host, Partner, User, btf: StatementBtf);

        ServerTestHelpers.ReadReturnCodes(Deserialize(await pipeline.ProcessAsync(request, _ct)))
            .Should().Be(("000000", "000000"));
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (ServiceProvider Provider, IEbicsRequestPipeline Pipeline, IMasterDataManager Master) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (provider, provider.GetRequiredService<IEbicsRequestPipeline>(), provider.GetRequiredService<IMasterDataManager>());
    }

    private static SubscriberKeyRef KeyRef() => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

    private async Task SeedReadyAsync(IMasterDataManager master, params SubscriberPermission[] permissions)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user, permissions: permissions), _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
