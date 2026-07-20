using System.Text;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Download;
using EBICO.Core;
using EBICO.Core.ReturnCodes;

namespace EBICO.Tests.Connector.Download;

/// <summary>
/// Behaviour tests for the client-side send validation (send-pipeline stage 1) on the download path. They
/// prove the opt-in client-side allow-list rejects an unauthorised order type locally (fast-fail, no
/// round-trip) with the <c>090003</c> return code the bank would report — including administrative order
/// types such as <c>HTD</c> — while an empty allow-list defers authorisation to the server, and that using
/// an upload order type on a download throws before any transport.
/// </summary>
public class DownloadValidationTests
{
    private static readonly byte[] SampleStatement = Encoding.UTF8.GetBytes(
        "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.08\"><BkToCstmrStmt><GrpHdr>" +
        "<MsgId>C53-VALIDATION-SAMPLE</MsgId><CreDtTm>2026-07-16T09:00:00</CreDtTm></GrpHdr>" +
        "<Stmt><Id>STMT-0001</Id></Stmt></BkToCstmrStmt></Document>");

    public static TheoryData<EbicsVersion> Versions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Allow_list_permitting_the_order_type_lets_the_download_through(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, allowedOrderTypes: ["STA"], ct: ct);

        var result = await harness.Client.Send(new StaDownloadRequest(), ct);

        result.IsSuccess.Should().BeTrue();
        harness.Server.InitRequestCount.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Allow_list_excluding_the_order_type_is_rejected_locally_without_a_round_trip(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, allowedOrderTypes: ["CCT"], ct: ct);

        var result = await harness.Client.Send(new StaDownloadRequest(), ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthorisationOrderTypeFailed.Code);
        harness.Server.InitRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Empty_allow_list_defers_authorisation_to_the_server(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var result = await harness.Client.Send(new StaDownloadRequest(), ct);

        result.IsSuccess.Should().BeTrue();
        harness.Server.InitRequestCount.Should().Be(1);
    }

    // Administrative order types (HTD/…) are kept as their AdminOrderType, but their own code is still the
    // effective authorisation key, so the allow-list applies to them as well.
    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Administrative_order_type_is_subject_to_the_allow_list(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, allowedOrderTypes: ["C53"], ct: ct);

        var result = await harness.Client.Send(new HtdDownloadRequest(), ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthorisationOrderTypeFailed.Code);
        harness.Server.InitRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Upload_order_type_on_a_download_throws_before_any_transport(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await DownloadTestHarness.CreateAsync(version, SampleStatement, ct: ct);

        var act = async () => await harness.Client.Send(new DownloadRequest { OrderType = "CCT" }, ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
        harness.Server.InitRequestCount.Should().Be(0);
    }
}
