using System.Text;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Connector.Upload;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.ReturnCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EBICO.Tests.Connector.Upload;

/// <summary>
/// Round-trip and behaviour tests for the upload API (issue #48) across all supported versions: a request
/// is built, sent through the <see cref="FakeUploadServer"/>, and the bytes the client uploaded are
/// decoded exactly as the server would to confirm the compress → E002-encrypt → segment pipeline is
/// recoverable; negative cases confirm server return codes surface as <see cref="EbicsResult{T}"/> failures.
/// </summary>
public class UploadRequestHandlerTests
{
    private static readonly byte[] SamplePain001 = Encoding.UTF8.GetBytes(
        "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pain.001.001.09\"><CstmrCdtTrfInitn><GrpHdr><MsgId>CCT-1</MsgId></GrpHdr></CstmrCdtTrfInitn></Document>");

    private static readonly byte[] SamplePain008 = Encoding.UTF8.GetBytes(
        "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pain.008.001.08\"><CstmrDrctDbtInitn><GrpHdr><MsgId>CDD-1</MsgId></GrpHdr></CstmrDrctDbtInitn></Document>");

    public static TheoryData<EbicsVersion> Versions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Cct_upload_succeeds_and_round_trips(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        var result = await harness.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

        result.IsSuccess.Should().BeTrue();
        result.ReturnCode.Should().Be(EbicsReturnCode.OkCode);
        result.Value!.TransactionId.Should().Be(Convert.ToHexString(harness.Server.TransactionId));
        result.Value.NumSegments.Should().Be(1);

        harness.Server.InitRequestCount.Should().Be(1);
        harness.Server.TransferRequestCount.Should().Be(1);
        harness.Server.SignatureData.Should().NotBeNull().And.NotBeEmpty();
        harness.DecodeUploadedOrderData().Should().Equal(SamplePain001);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Cct_upload_carries_the_expected_order_identity(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        await harness.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

        if (version == EbicsVersion.H005)
        {
            harness.Server.HeaderOrderType.Should().Be("BTU");
            harness.Server.Btf.Should().NotBeNull();
            harness.Server.Btf!.Value.Service.Should().Be("SCT");
        }
        else
        {
            harness.Server.HeaderOrderType.Should().Be("CCT");
            harness.Server.OrderAttribute.Should().Be("Dzhnn");
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Large_payload_is_split_across_multiple_segments(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        // A tiny segment size forces the encrypted ciphertext across several transfer messages.
        var result = await harness.Client.Send(
            new UploadRequest { OrderData = SamplePain001, OrderType = "CCT", MaxSegmentSizeBytes = 16 }, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.NumSegments.Should().BeGreaterThan(1);
        harness.Server.TransferRequestCount.Should().Be(result.Value.NumSegments);
        harness.Server.NumSegments.Should().Be((ulong)result.Value.NumSegments);
        harness.DecodeUploadedOrderData().Should().Equal(SamplePain001);
    }

    [Theory]
    [MemberData(nameof(ConvenienceCases))]
    public async Task Convenience_requests_carry_the_expected_order_identity(
        EbicsVersion version, string orderTypeCode, string btfService, string? btfOption, IEbicsRequest<UploadResult> request)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        var result = await harness.Client.Send(request, ct);

        result.IsSuccess.Should().BeTrue();
        if (version == EbicsVersion.H005)
        {
            harness.Server.HeaderOrderType.Should().Be("BTU");
            harness.Server.Btf.Should().NotBeNull();
            harness.Server.Btf!.Value.Service.Should().Be(btfService);
            harness.Server.Btf.Value.Option.Should().Be(btfOption);
        }
        else
        {
            harness.Server.HeaderOrderType.Should().Be(orderTypeCode);
        }
    }

    public static TheoryData<EbicsVersion, string, string, string?, IEbicsRequest<UploadResult>> ConvenienceCases()
    {
        var data = new TheoryData<EbicsVersion, string, string, string?, IEbicsRequest<UploadResult>>();
        foreach (var version in new[] { EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005 })
        {
            data.Add(version, "CDD", "SDD", "COR", new CddUploadRequest { Pain008 = SamplePain008 });
            data.Add(version, "CDB", "SDD", "B2B", new CdbUploadRequest { Pain008 = SamplePain008 });
            data.Add(version, "CIP", "SCT", "INST", new CipUploadRequest { Pain001 = SamplePain001 });
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Initialisation_failure_is_surfaced_and_no_transfer_is_sent(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new FakeUploadServerOptions { InitReturnCode = EbicsReturnCode.AuthorisationOrderTypeFailed };
        using var harness = await UploadTestHarness.CreateAsync(version, options, ct);

        var result = await harness.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthorisationOrderTypeFailed.Code);
        harness.Server.TransferRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Transfer_rejection_is_surfaced_as_failure(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new FakeUploadServerOptions { LastTransferReturnCode = EbicsReturnCode.InvalidOrderDataFormat };
        using var harness = await UploadTestHarness.CreateAsync(version, options, ct);

        var result = await harness.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.InvalidOrderDataFormat.Code);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Unknown_transaction_id_is_surfaced_as_failure(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new FakeUploadServerOptions { LastTransferReturnCode = EbicsReturnCode.TxUnknownTxid };
        using var harness = await UploadTestHarness.CreateAsync(version, options, ct);

        var result = await harness.Client.Send(new CddUploadRequest { Pain008 = SamplePain008 }, ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.TxUnknownTxid.Code);
    }

    [Fact]
    public async Task Upload_without_a_bank_encryption_key_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new FakeTransport(_ => new EbicsHttpResponse { StatusCode = 200, Payload = ReadOnlyMemory<byte>.Empty });

        var services = new ServiceCollection();
        services.AddEbicoConnector(o =>
        {
            o.Url = "https://bank.example/ebics";
            o.HostId = "HOST";
            o.PartnerId = "PART";
            o.UserId = "USER";
            o.Version = EbicsVersion.H005;
        });
        services.AddEbicoUpload();
        services.RemoveAll<ITransport>();
        services.AddSingleton<ITransport>(transport);
        using var provider = services.BuildServiceProvider();

        var keys = provider.GetRequiredService<IKeyStore>();
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Signature, RsaKeyMaterial.Generate(), ct);
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, RsaKeyMaterial.Generate(), ct);
        // Deliberately no bank encryption key (HPB not run).

        var client = provider.GetRequiredService<IEbicsClient>();

        var act = async () => await client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);
        await act.Should().ThrowAsync<EbicsConfigurationException>();
    }
}
