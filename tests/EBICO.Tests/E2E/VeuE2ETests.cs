extern alias EbicoServer;
using System.Text;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Download;
using EBICO.Connector.Upload;
using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Server.State;
using EBICO.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.E2E;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end distributed electronic signature (VEU/EDS) through the real connector (issue #124). The
/// server side has supported HVU/HVZ/HVD/HVT/HVE/HVS since #42, but the connector could not drive it:
/// H005 uploads insisted on a BTF (which the administrative VEU orders do not have), there was no way to
/// pass the referenced <c>OrderID</c>, and the order attribute was hard-wired to <c>DZHNN</c> so no order
/// could ever be parked in the first place. These tests walk the whole loop.
/// </summary>
/// <remarks>
/// The signing subscriber needs a <b>bank-technical</b> permission (E/A/B) for the underlying order type
/// — the emulator authorises HVE against the parked order's own type, not against "HVE" — which is why
/// these harnesses seed <see cref="SignatureClass.E"/> for CCT rather than the default T.
/// </remarks>
public class VeuE2ETests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public VeuE2ETests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>The EBICS versions covered by the end-to-end matrix.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    /// <summary>Permissions that let the subscriber both submit and bank-technically sign a CCT.</summary>
    private static SubscriberPermission[] SigningPermissions =>
        [new("CCT", SignatureClass.E), new("HVU", SignatureClass.T), new("HVD", SignatureClass.T),
         new("HVT", SignatureClass.T), new("HVZ", SignatureClass.T), new("HVE", SignatureClass.E),
         new("HVS", SignatureClass.E)];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task ParkedUpload_IsHeldForSignatures_AndListedByHvu(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUPARK", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        // DistributedSignature is what turns an ordinary CCT into a parked order: H005 emits the
        // BTUOrderParams/SignatureFlag, H003/H004 switch the order attribute from DZHNN to OZHNN.
        var upload = await harness.Client.Send(
            new UploadRequest
            {
                OrderType = "CCT",
                OrderData = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([25.00m])),
                DistributedSignature = true,
            },
            _ct);

        upload.IsSuccess.Should().BeTrue($"parked CCT upload failed: {upload.ReturnCode} {upload.ReturnText}");

        // The order is held in the VEU store rather than executed. The submitter holds a bank-technical
        // permission for CCT, so the emulator counts their submission as the first signature — the order
        // waits for the second one (the default VeuRequiredSignatures is 2).
        var open = await OpenOrdersAsync(harness);
        open.Should().ContainSingle();
        open[0].EffectiveOrderType.Should().Be("CCT");
        open[0].NumSigDone.Should().Be(1);
        open[0].NumSigRequired.Should().Be(2);
        open[0].IsFullySigned.Should().BeFalse();

        // ...and HVU reports it to the client.
        var overview = await harness.Client.Send(new HvuDownloadRequest(), _ct);
        overview.IsSuccess.Should().BeTrue($"HVU failed: {overview.ReturnCode} {overview.ReturnText}");
        Encoding.UTF8.GetString(overview.Value!.OrderData.Span).Should().Contain(open[0].OrderId);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task ParkedUpload_WorksThroughTheSepaConvenienceRequest(EbicsVersion version)
    {
        // A SEPA payment is exactly the order a customer submits for multi-person approval, so the flag
        // has to exist on CctUploadRequest and friends — not only on the generic UploadRequest (#124).
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUCONV", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var upload = await harness.Client.Send(
            new CctUploadRequest
            {
                Pain001 = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([25.00m])),
                DistributedSignature = true,
            },
            _ct);

        upload.IsSuccess.Should().BeTrue($"parked CCT upload failed: {upload.ReturnCode} {upload.ReturnText}");
        (await OpenOrdersAsync(harness)).Should().ContainSingle("the payment must be parked, not executed");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task UnflaggedUpload_IsExecutedImmediately(EbicsVersion version)
    {
        // The counterpart: without the flag nothing lands in the VEU store — the default stays "execute".
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUNOFLAG", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var upload = await harness.Client.Send(
            new CctUploadRequest { Pain001 = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([25.00m])) }, _ct);

        upload.IsSuccess.Should().BeTrue();
        (await OpenOrdersAsync(harness)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task HveSignature_FromASecondSubscriber_ReleasesTheOrder(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUSIGN", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        (await ParkAsync(harness)).IsSuccess.Should().BeTrue();
        var parked = (await OpenOrdersAsync(harness)).Single();

        // The submitter already counts as one signer, so the releasing HVE must come from someone else —
        // which is the whole point of the distributed signature.
        await using var coSigner = await harness.AddCoSignerAsync(_factory, "B", SigningPermissions, _ct);

        var signed = await coSigner.Client.Send(
            new HveUploadRequest { Order = new VeuOrderReference { OrderId = parked.OrderId, OrderType = "CCT" } },
            _ct);

        signed.IsSuccess.Should().BeTrue($"HVE failed: {signed.ReturnCode} {signed.ReturnText}");
        (await OpenOrdersAsync(harness)).Should().BeEmpty("a fully signed order is released and removed");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task HveSignature_FromTheSubmitter_IsRejectedAsADuplicate(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUDUP", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        (await ParkAsync(harness)).IsSuccess.Should().BeTrue();
        var parked = (await OpenOrdersAsync(harness)).Single();

        // Signing one's own submission a second time must not advance the counter.
        var again = await harness.Client.Send(
            new HveUploadRequest { Order = new VeuOrderReference { OrderId = parked.OrderId, OrderType = "CCT" } },
            _ct);

        again.IsSuccess.Should().BeFalse();
        (await OpenOrdersAsync(harness)).Single().NumSigDone.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task HvsCancellation_RemovesTheParkedOrder(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUCANCEL", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        (await ParkAsync(harness)).IsSuccess.Should().BeTrue();
        var parked = (await OpenOrdersAsync(harness)).Single();

        var cancelled = await harness.Client.Send(
            new HvsUploadRequest { Order = new VeuOrderReference { OrderId = parked.OrderId, OrderType = "CCT" } },
            _ct);

        cancelled.IsSuccess.Should().BeTrue($"HVS failed: {cancelled.ReturnCode} {cancelled.ReturnText}");
        (await OpenOrdersAsync(harness)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task HvdDetail_ResolvesTheReferencedOrder(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUDETAIL", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        (await ParkAsync(harness)).IsSuccess.Should().BeTrue();
        var parked = (await OpenOrdersAsync(harness)).Single();

        // Proves the OrderID reaches the server in the version-specific HVD order params: without it the
        // server has nothing to look up and answers 090005.
        var detail = await harness.Client.Send(
            new HvdDownloadRequest { Order = new VeuOrderReference { OrderId = parked.OrderId, OrderType = "CCT" } },
            _ct);

        detail.IsSuccess.Should().BeTrue($"HVD failed: {detail.ReturnCode} {detail.ReturnText}");

        // The response describes the referenced order (the id itself lives in the request, not the reply).
        var xml = Encoding.UTF8.GetString(detail.Value!.OrderData.Span);
        xml.Should().Contain("HVDResponseOrderData");
        xml.Should().Contain($"<UserID>{harness.UserId.Value}</UserID>", "the submitter is reported as signer");
        xml.Should().Contain($"<OrderDataSize>{parked.OrderData.Length}</OrderDataSize>");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task HvdDetail_ForAnUnknownOrderId_ReportsNoData(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUDETAILGONE", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        (await ParkAsync(harness)).IsSuccess.Should().BeTrue();

        // The counterpart to the test above: a different id finds nothing, which is what proves the id is
        // actually transmitted and matched rather than ignored.
        var detail = await harness.Client.Send(
            new HvdDownloadRequest { Order = new VeuOrderReference { OrderId = "ZZ99", OrderType = "CCT" } },
            _ct);

        detail.IsSuccess.Should().BeFalse();
        detail.ReturnCode.Should().Be(EbicsReturnCode.NoDownloadDataAvailable.Code);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task HveWithAnUnknownOrderId_IsRejected(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUUNKNOWN", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var result = await harness.Client.Send(
            new HveUploadRequest { Order = new VeuOrderReference { OrderId = "ZZ99", OrderType = "CCT" } },
            _ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.InvalidOrderIdentifier.Code);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task GenericVeuUpload_WithoutAnOrderReference_FailsFastClientSide(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "VEUNOREF", permissions: SigningPermissions, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        // Nothing to act on: rejected before any crypto or transport, with a message naming the gap.
        var act = async () => await harness.Client.Send(
            new UploadRequest { OrderType = "HVE", OrderData = Encoding.UTF8.GetBytes("<HVEOrderData/>") }, _ct);

        (await act.Should().ThrowAsync<EbicsConfigurationException>())
            .WithMessage("*must reference the parked order*");
    }

    // Submits a CCT flagged for the distributed signature, so the bank parks it.
    private Task<EbicsResult<UploadResult>> ParkAsync(EbicsE2EHarness harness)
        => harness.Client.Send(
            new UploadRequest
            {
                OrderType = "CCT",
                OrderData = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([25.00m])),
                DistributedSignature = true,
            },
            _ct);

    private static async Task<IReadOnlyList<OpenVeuOrder>> OpenOrdersAsync(EbicsE2EHarness harness)
    {
        var store = harness.ServerServices.GetRequiredService<IOpenVeuStore>();
        return await store.ListAsync(harness.HostId, harness.PartnerId, CancellationToken.None);
    }
}
