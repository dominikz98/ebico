using System.Text;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Download;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.ReturnCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EBICO.Tests.Connector.Download;

/// <summary>
/// Round-trip and behaviour tests for the download API (issue #49) across all supported versions: a
/// request is sent through the <see cref="FakeDownloadServer"/> (which encodes the payload exactly as the
/// server would) and the connector's reassemble → E002-decrypt → decompress pipeline is proven to
/// recover the original bytes. Further tests cover the receipt phase, the convenience order identity, the
/// reporting period, the parsing hook and the negative cases (server return codes and crypto/parse failures).
/// </summary>
public class DownloadRequestHandlerTests
{
    private static readonly byte[] SampleStatement = Encoding.UTF8.GetBytes(
        "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.08\"><BkToCstmrStmt><GrpHdr>" +
        "<MsgId>C53-DOWNLOAD-SAMPLE-0001</MsgId><CreDtTm>2026-07-16T09:00:00</CreDtTm></GrpHdr>" +
        "<Stmt><Id>STMT-0001</Id><Acct><Id><IBAN>DE02120300000000202051</IBAN></Id></Acct></Stmt>" +
        "</BkToCstmrStmt></Document>");

    public static TheoryData<EbicsVersion> Versions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Download_succeeds_and_round_trips(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var result = await harness.Client.Send(new C53DownloadRequest(), ct);

        result.IsSuccess.Should().BeTrue();
        result.ReturnCode.Should().Be(EbicsReturnCode.DownloadPostprocessDone.Code);
        result.Value!.TransactionId.Should().Be(Convert.ToHexString(harness.Server.TransactionId));
        result.Value.NumSegments.Should().Be(1);
        result.Value.OrderData.ToArray().Should().Equal(SampleStatement);

        harness.Server.InitRequestCount.Should().Be(1);
        harness.Server.TransferRequestCount.Should().Be(0);
        harness.Server.ReceiptRequestCount.Should().Be(1);
        harness.Server.ReceiptCode.Should().Be((byte)0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Multi_segment_download_reassembles(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        // A tiny segment size forces the encrypted ciphertext across several transfer messages.
        var options = new FakeDownloadServerOptions { SegmentSizeBytes = 16 };
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, options, ct);

        var result = await harness.Client.Send(new C53DownloadRequest(), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.NumSegments.Should().BeGreaterThan(1);
        result.Value.OrderData.ToArray().Should().Equal(SampleStatement);
        // Segment 1 arrives with the initialisation; the rest come over transfer messages.
        harness.Server.TransferRequestCount.Should().Be(result.Value.NumSegments - 1);
        harness.Server.ReceiptRequestCount.Should().Be(1);
        harness.Server.ReceiptCode.Should().Be((byte)0);
    }

    [Theory]
    [MemberData(nameof(ConvenienceCases))]
    public async Task Convenience_requests_carry_the_expected_order_identity(
        EbicsVersion version, string expectedOrderType, string? expectedBtfService, IEbicsRequest<DownloadResult> request)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var result = await harness.Client.Send(request, ct);

        result.IsSuccess.Should().BeTrue();
        if (version == EbicsVersion.H005 && expectedBtfService is not null)
        {
            // Statement/report downloads become BTD + a BTF on H005.
            harness.Server.HeaderOrderType.Should().Be("BTD");
            harness.Server.Btf.Should().NotBeNull();
            harness.Server.Btf!.Value.Service.Should().Be(expectedBtfService);
        }
        else
        {
            // H003/H004 use the classical code directly; H005 administrative orders keep their AdminOrderType.
            harness.Server.HeaderOrderType.Should().Be(expectedOrderType);
            harness.Server.Btf.Should().BeNull();
        }
    }

    public static TheoryData<EbicsVersion, string, string?, IEbicsRequest<DownloadResult>> ConvenienceCases()
    {
        var data = new TheoryData<EbicsVersion, string, string?, IEbicsRequest<DownloadResult>>();
        foreach (var version in new[] { EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005 })
        {
            // Statement/report downloads (mapped to a BTF on H005).
            data.Add(version, "STA", "EOP", new StaDownloadRequest());
            data.Add(version, "VMK", "STM", new VmkDownloadRequest());
            data.Add(version, "C53", "EOP", new C53DownloadRequest());
            data.Add(version, "C52", "STM", new C52DownloadRequest());
            data.Add(version, "C54", "EOP", new C54DownloadRequest());

            // Administrative status/protocol downloads (AdminOrderType on H005, no BTF).
            data.Add(version, "HTD", null, new HtdDownloadRequest());
            data.Add(version, "HKD", null, new HkdDownloadRequest());
            data.Add(version, "HAA", null, new HaaDownloadRequest());
            data.Add(version, "HPD", null, new HpdDownloadRequest());
            data.Add(version, "HAC", null, new HacDownloadRequest());
            data.Add(version, "PTK", null, new PtkDownloadRequest());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Reporting_period_is_forwarded(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 3, 31);
        var result = await harness.Client.Send(new C53DownloadRequest { Period = new DateRange(from, to) }, ct);

        result.IsSuccess.Should().BeTrue();
        harness.Server.PeriodStart.Should().Be(from);
        harness.Server.PeriodEnd.Should().Be(to);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Parse_hook_produces_the_parsed_value(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var result = await harness.Client.Send(
            new C53DownloadRequest { Parse = bytes => Encoding.UTF8.GetString(bytes.Span) }, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ParsedAs<string>().Should().Be(Encoding.UTF8.GetString(SampleStatement));
        harness.Server.ReceiptCode.Should().Be((byte)0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Generic_request_with_file_format_uses_fdl_on_h003_h004(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var request = version == EbicsVersion.H005
            ? new DownloadRequest { OrderType = "STA" }
            : new DownloadRequest { FileFormat = "camt.053" };
        var result = await harness.Client.Send(request, ct);

        result.IsSuccess.Should().BeTrue();
        harness.Server.HeaderOrderType.Should().Be(version == EbicsVersion.H005 ? "BTD" : "FDL");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Initialisation_failure_is_surfaced_and_no_transfer_or_receipt(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new FakeDownloadServerOptions { InitReturnCode = EbicsReturnCode.NoDownloadDataAvailable };
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, options, ct);

        var result = await harness.Client.Send(new C53DownloadRequest(), ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.NoDownloadDataAvailable.Code);
        harness.Server.TransferRequestCount.Should().Be(0);
        harness.Server.ReceiptRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Transfer_failure_is_surfaced_as_failure(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        // Tiny segments force a transfer phase; that transfer is rejected with an unknown transaction id.
        var options = new FakeDownloadServerOptions { SegmentSizeBytes = 16, TransferReturnCode = EbicsReturnCode.TxUnknownTxid };
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, options, ct);

        var result = await harness.Client.Send(new C53DownloadRequest(), ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.TxUnknownTxid.Code);
        harness.Server.ReceiptRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Undecryptable_data_sends_a_negative_receipt_and_throws(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new FakeDownloadServerOptions { EncryptForWrongKey = true };
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, options, ct);

        var act = async () => await harness.Client.Send(new C53DownloadRequest(), ct);

        await act.Should().ThrowAsync<EbicsConnectorException>();
        harness.Server.ReceiptCode.Should().Be((byte)1);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Parse_hook_failure_sends_a_negative_receipt_and_rethrows(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var act = async () => await harness.Client.Send(
            new C53DownloadRequest { Parse = _ => throw new InvalidOperationException("boom") }, ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Server.ReceiptCode.Should().Be((byte)1);
    }

    [Fact]
    public async Task Download_without_a_subscriber_encryption_key_throws()
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
        services.AddEbicoDownload();
        services.RemoveAll<ITransport>();
        services.AddSingleton<ITransport>(transport);
        using var provider = services.BuildServiceProvider();

        var keys = provider.GetRequiredService<IKeyStore>();
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, RsaKeyMaterial.Generate(), ct);
        // Deliberately no subscriber encryption key (onboarding not completed).

        var client = provider.GetRequiredService<IEbicsClient>();

        var act = async () => await client.Send(new C53DownloadRequest(), ct);
        await act.Should().ThrowAsync<EbicsConfigurationException>();
    }
}
