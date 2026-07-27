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

    // An administrative upload order type has no BTF. H005 must therefore keep it as the AdminOrderType
    // instead of demanding a BTF — exactly what the download path has always done. Before #124 this threw
    // "H005 uploads require a business transaction format (BTF)" and the VEU uploads never reached the wire.
    [Fact]
    public async Task H005_administrative_upload_order_type_travels_as_the_admin_order_type()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(EbicsVersion.H005, ct: ct);

        var result = await harness.Client.Send(
            new UploadRequest
            {
                OrderType = "HVE",
                OrderData = SamplePain001,
                Veu = new VeuOrderReference { OrderId = "A1B2", OrderType = "CCT" },
            },
            ct);

        result.IsSuccess.Should().BeTrue();
        harness.Server.InitRequestCount.Should().Be(1);
        harness.Server.HeaderOrderType.Should().Be("HVE", "administrative orders are not BTU business transactions");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Veu_upload_without_an_order_reference_throws_before_any_transport(EbicsVersion version)
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(version, ct: ct);

        var act = async () => await harness.Client.Send(
            new UploadRequest { OrderData = SamplePain001, OrderType = "HVS" }, ct);

        (await act.Should().ThrowAsync<EbicsConfigurationException>())
            .WithMessage("*must reference the parked order*");
        harness.Server.InitRequestCount.Should().Be(0);
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

    // Behaviour change in #124: an H005 order type without a BTF mapping is no longer a client-side error.
    // It is submitted as the AdminOrderType and the bank decides — mirroring the download path, which has
    // always worked that way. Rejecting locally made the administrative uploads (VEU) unreachable, and the
    // BTF catalogue is an explicitly best-effort seed, so it is not a reliable "does this exist" oracle.
    // An order type the bank does not know still fails, just with the bank's 091006 instead of an exception.
    [Fact]
    public async Task H005_upload_with_an_unmapped_order_type_travels_as_the_admin_order_type()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(EbicsVersion.H005, ct: ct);

        var result = await harness.Client.Send(
            new UploadRequest { OrderData = SamplePain001, OrderType = "ZZZ" }, ct);

        result.IsSuccess.Should().BeTrue("the fake server accepts whatever it is sent");
        harness.Server.InitRequestCount.Should().Be(1);
        harness.Server.HeaderOrderType.Should().Be("ZZZ");
    }

    [Fact]
    public async Task H005_upload_without_an_order_type_or_btf_still_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = await UploadTestHarness.CreateAsync(EbicsVersion.H005, ct: ct);

        // Nothing at all to identify the order with remains a configuration error.
        var act = async () => await harness.Client.Send(new UploadRequest { OrderData = SamplePain001 }, ct);

        await act.Should().ThrowAsync<EbicsConfigurationException>();
        harness.Server.InitRequestCount.Should().Be(0);
    }
}
