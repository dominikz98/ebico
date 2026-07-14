using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Btf;
using EBICO.Core.Domain;
using EBICO.Core.Payments;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using EBICO.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// End-to-end tests for the SEPA payment upload orders (issue #39): CCT/CIP (<c>pain.001</c>) and
/// CDD/CDB (<c>pain.008</c>) submitted through the upload transaction, across the three submission
/// conventions (H005 BTU + BTF, H003/H004 classical order type direct, H003/H004 generic FUL +
/// <c>FileFormat</c>). A valid payload is accepted (<c>000000</c>) and its <c>pain.002</c> status report
/// filed for later download; an invalid payload is rejected (<c>090004</c>) and nothing is filed. Driven
/// through <see cref="EbicsRequestPipeline"/> with requests built from the committed Core bindings.
/// </summary>
public class PaymentUploadTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";
    private const string StatusReportOrderType = "PSR";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static readonly BusinessTransactionFormat CctBtf = new("SCT", messageName: "pain.001");
    private static readonly BusinessTransactionFormat CddBtf = new("SDD", option: "COR", messageName: "pain.008");
    private static readonly BusinessTransactionFormat CdbBtf = new("SDD", option: "B2B", messageName: "pain.008");

    public static TheoryData<EbicsVersion> AllVersions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];
    public static TheoryData<EbicsVersion> ClassicVersions => [EbicsVersion.H003, EbicsVersion.H004];

    // --- Credit transfer (CCT) -------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task CreditTransfer_ValidPayload_AcceptedAndStatusReportFiled(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, downloadData, _) = BuildServer();
        await SeedReadyAsync(master);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var pain = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([100.00m, 50.00m], messageId: "MSG-CCT-1"));
        var upload = version == EbicsVersion.H005
            ? ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, btf: CctBtf)
            : ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, orderType: PaymentOrderTypes.CreditTransfer);

        var final = await RunUploadAsync(pipeline, version, upload);

        ServerTestHelpers.ReadReturnCodes(final).Should().Be(("000000", "000000"));
        (await downloadData.CountAsync(KeyRef(), StatusReportOrderType, _ct)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(ClassicVersions))]
    public async Task CreditTransfer_ViaFulFileFormat_Accepted(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, downloadData, _) = BuildServer();
        await SeedReadyAsync(master);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var pain = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([42.00m], messageId: "MSG-FUL-CCT"));
        // Generic file upload: OrderType "FUL" with the pain format in FULOrderParams/FileFormat.
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion,
            orderType: "FUL", fileFormat: "pain.001.001.09");

        var final = await RunUploadAsync(pipeline, version, upload);

        ServerTestHelpers.ReadReturnCodes(final).Should().Be(("000000", "000000"));
        (await downloadData.CountAsync(KeyRef(), StatusReportOrderType, _ct)).Should().Be(1);
    }

    // --- Direct debit (CDD core / CDB b2b) -------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task DirectDebitCore_ValidPayload_Accepted(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, downloadData, _) = BuildServer();
        await SeedReadyAsync(master);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var pain = Encoding.UTF8.GetBytes(PainSamples.DirectDebit([75.00m], messageId: "MSG-CDD-1"));
        var upload = version == EbicsVersion.H005
            ? ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, btf: CddBtf)
            : ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, orderType: PaymentOrderTypes.DirectDebitCore);

        var final = await RunUploadAsync(pipeline, version, upload);

        ServerTestHelpers.ReadReturnCodes(final).Should().Be(("000000", "000000"));
        (await downloadData.CountAsync(KeyRef(), StatusReportOrderType, _ct)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task DirectDebitB2B_ValidPayload_Accepted(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, downloadData, _) = BuildServer();
        await SeedReadyAsync(master);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var pain = Encoding.UTF8.GetBytes(PainSamples.DirectDebit([12.34m], messageId: "MSG-CDB-1"));
        var upload = version == EbicsVersion.H005
            ? ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, btf: CdbBtf)
            : ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, orderType: PaymentOrderTypes.DirectDebitB2B);

        var final = await RunUploadAsync(pipeline, version, upload);

        ServerTestHelpers.ReadReturnCodes(final).Should().Be(("000000", "000000"));
        (await downloadData.CountAsync(KeyRef(), StatusReportOrderType, _ct)).Should().Be(1);
    }

    // --- Status report content -------------------------------------------------------------

    [Fact]
    public async Task CreditTransfer_FiledStatusReport_IsPain002EchoingTheOriginalMessageId()
    {
        var (pipeline, master, bankKeys, downloadData, _) = BuildServer();
        await SeedReadyAsync(master);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        var pain = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([100.00m], messageId: "MSG-STATUS-1", messageVersion: "pain.001.001.09"));
        var upload = ServerTestHelpers.BuildUploadInitRequest(
            EbicsVersion.H005, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, btf: CctBtf);

        await RunUploadAsync(pipeline, EbicsVersion.H005, upload);

        var report = await downloadData.TryDequeueAsync(
            new DownloadDataRequest(EbicsVersion.H005, KeyRef(), StatusReportOrderType), _ct);
        report.Should().NotBeNull();

        XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:pain.002.001.03";
        var document = XDocument.Parse(Encoding.UTF8.GetString(report!));
        var status = document.Root!.Element(ns + "CstmrPmtStsRpt")!.Element(ns + "OrgnlGrpInfAndSts")!;

        status.Element(ns + "OrgnlMsgId")!.Value.Should().Be("MSG-STATUS-1");
        status.Element(ns + "OrgnlMsgNmId")!.Value.Should().Be("pain.001.001.09");
        status.Element(ns + "GrpSts")!.Value.Should().Be(PainStatusReportBuilder.AcceptedGroupStatus);
    }

    // --- Rejection -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task CreditTransfer_InvalidPayload_RejectedAndNothingFiled(EbicsVersion version)
    {
        var (pipeline, master, bankKeys, downloadData, store) = BuildServer();
        await SeedReadyAsync(master);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        // A structurally invalid credit transfer: the control sum does not match the instructed amount.
        var invalid = PainSamples.CreditTransfer([100.00m], messageId: "MSG-BAD")
            .Replace("<CtrlSum>100.00</CtrlSum>", "<CtrlSum>999.00</CtrlSum>", StringComparison.Ordinal);
        var pain = Encoding.UTF8.GetBytes(invalid);
        var upload = version == EbicsVersion.H005
            ? ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, btf: CctBtf)
            : ServerTestHelpers.BuildUploadInitRequest(version, Host, Partner, User, pain, bank.Encryption, bank.EncryptionVersion, orderType: PaymentOrderTypes.CreditTransfer);

        var initEnvelope = Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        var transfer = ServerTestHelpers.BuildUploadTransferRequest(version, Host, transactionId!, 1, true, upload.Segments[0]);

        var final = Deserialize(await pipeline.ProcessAsync(transfer, _ct));

        ServerTestHelpers.ReadReturnCodes(final).BodyCode.Should().Be("090004");
        (await downloadData.CountAsync(KeyRef(), StatusReportOrderType, _ct)).Should().Be(0);

        // A rejected payload is not retained on the transaction.
        store.TryGet(Convert.ToHexString(transactionId!), out var transaction).Should().BeTrue();
        transaction!.IsComplete.Should().BeFalse();
    }

    // --- Helpers ---------------------------------------------------------------------------

    private async Task<IEbicsEnvelope> RunUploadAsync(IEbicsRequestPipeline pipeline, EbicsVersion version, ServerTestHelpers.UploadRequest upload)
    {
        var initEnvelope = Deserialize(await pipeline.ProcessAsync(upload.InitXml, _ct));
        ServerTestHelpers.ReadReturnCodes(initEnvelope).Should().Be(("000000", "000000"));
        var transactionId = ServerTestHelpers.ReadTransactionId(initEnvelope);
        transactionId.Should().NotBeNull();

        IEbicsEnvelope last = initEnvelope;
        for (var i = 0; i < upload.Segments.Count; i++)
        {
            var transfer = ServerTestHelpers.BuildUploadTransferRequest(
                version, Host, transactionId!, (ulong)(i + 1), i == upload.Segments.Count - 1, upload.Segments[i]);
            last = Deserialize(await pipeline.ProcessAsync(transfer, _ct));
        }

        return last;
    }

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerBankKeyStore BankKeys, IDownloadDataProvider DownloadData, IUploadTransactionStore Store) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerBankKeyStore>(),
            provider.GetRequiredService<IDownloadDataProvider>(),
            provider.GetRequiredService<IUploadTransactionStore>());
    }

    private static SubscriberKeyRef KeyRef() => new(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

    private async Task SeedReadyAsync(IMasterDataManager master)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(
            new Subscriber(host, partner, user, permissions:
            [
                new SubscriberPermission(PaymentOrderTypes.CreditTransfer, SignatureClass.T),
                new SubscriberPermission(PaymentOrderTypes.InstantCreditTransfer, SignatureClass.T),
                new SubscriberPermission(PaymentOrderTypes.DirectDebitCore, SignatureClass.T),
                new SubscriberPermission(PaymentOrderTypes.DirectDebitB2B, SignatureClass.T),
            ]),
            _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
