using System.Text;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Upload;
using EBICO.Core;
using EBICO.Core.ReturnCodes;

namespace EBICO.Tests.Connector.Upload;

/// <summary>
/// Behaviour tests for the client-side send validation (send-pipeline stage 1) on the upload path. They
/// prove the structural/BTF checks throw <see cref="EbicsConfigurationException"/> before any transport,
/// and the opt-in client-side allow-list (<see cref="EbicsResult{T}"/> failure with <c>090003</c>) rejects
/// an unauthorised order type locally — a fast-fail with no round-trip (<c>InitRequestCount == 0</c>) — while
/// an empty allow-list defers authorisation to the server (no behaviour change).
/// </summary>
public class UploadValidationTests
{
    private static readonly byte[] SamplePain001 = Encoding.UTF8.GetBytes(
        "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pain.001.001.09\"><CstmrCdtTrfInitn><GrpHdr><MsgId>CCT-1</MsgId></GrpHdr></CstmrCdtTrfInitn></Document>");

    public static TheoryData<EbicsVersion> Versions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Allow_list_permitting_the_order_type_lets_the_upload_through(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, allowedOrderTypes: ["CCT"], ct: ct);

        var result = await harness.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

        result.IsSuccess.Should().BeTrue();
        harness.Server.InitRequestCount.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Allow_list_excluding_the_order_type_is_rejected_locally_without_a_round_trip(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, allowedOrderTypes: ["C53"], ct: ct);

        var result = await harness.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthorisationOrderTypeFailed.Code);
        harness.Server.InitRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Empty_allow_list_defers_authorisation_to_the_server(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        var result = await harness.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

        result.IsSuccess.Should().BeTrue();
        harness.Server.InitRequestCount.Should().Be(1);
    }

    // The allow-list is matched against the effective *classical* order type, not the H005 wire code (BTU):
    // "CCT" permits the H005 CCT upload (which goes out as BTU), while listing "BTU" denies it.
    [Fact]
    public async Task H005_allow_list_matches_the_classical_code_not_the_wire_btu()
    {
        var ct = TestContext.Current.CancellationToken;

        using (var permitted = await UploadTestHarness.CreateAsync(EbicsVersion.H005, allowedOrderTypes: ["CCT"], ct: ct))
        {
            var ok = await permitted.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

            ok.IsSuccess.Should().BeTrue();
            permitted.Server.HeaderOrderType.Should().Be("BTU");
        }

        using (var wrong = await UploadTestHarness.CreateAsync(EbicsVersion.H005, allowedOrderTypes: ["BTU"], ct: ct))
        {
            var denied = await wrong.Client.Send(new CctUploadRequest { Pain001 = SamplePain001 }, ct);

            denied.IsSuccess.Should().BeFalse();
            denied.ReturnCode.Should().Be(EbicsReturnCode.AuthorisationOrderTypeFailed.Code);
            wrong.Server.InitRequestCount.Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Empty_payload_throws_before_any_transport(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        var act = async () => await harness.Client.Send(
            new UploadRequest { OrderData = ReadOnlyMemory<byte>.Empty, OrderType = "CCT" }, ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
        harness.Server.InitRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Non_positive_segment_size_throws_before_any_transport(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        var act = async () => await harness.Client.Send(
            new UploadRequest { OrderData = SamplePain001, OrderType = "CCT", MaxSegmentSizeBytes = 0 }, ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
        harness.Server.InitRequestCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Download_order_type_on_an_upload_throws_before_any_transport(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        var act = async () => await harness.Client.Send(
            new UploadRequest { OrderData = SamplePain001, OrderType = "STA" }, ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
        harness.Server.InitRequestCount.Should().Be(0);
    }

    [Fact]
    public async Task H005_upload_with_an_unmapped_order_type_and_no_btf_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(EbicsVersion.H005, ct: ct);

        var act = async () => await harness.Client.Send(
            new UploadRequest { OrderData = SamplePain001, OrderType = "ZZZ" }, ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
        harness.Server.InitRequestCount.Should().Be(0);
    }
}
