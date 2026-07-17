extern alias EbicoServer;
using System.Text;
using AwesomeAssertions;
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
/// End-to-end SEPA credit-transfer upload (issue #57): the real connector uploads a pain.001 through the
/// full client-side pipeline (compress → E002 → ES → segment → X002) to the real in-process server, which
/// reassembles, decrypts, decompresses and validates it. The payload is read back out of the server's
/// transaction store, so the assertion is on the bytes the server actually recovered.
/// </summary>
public class UploadE2ETests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public UploadE2ETests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>The EBICS versions covered by the end-to-end matrix.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task CctUpload_RoundTrips_AndServerRecoversThePain001(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(_factory, version, "CCT", ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var pain = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([12.34m, 56.78m]));

        var result = await harness.Client.Send(new CctUploadRequest { Pain001 = pain }, _ct);

        result.IsSuccess.Should().BeTrue($"CCT upload failed: {result.ReturnCode} {result.ReturnText}");
        result.ReturnCode.Should().Be(EbicsReturnCode.OkCode);
        result.Value!.NumSegments.Should().Be(1);
        result.Value.TransactionId.Should().MatchRegex("^[0-9A-F]{32}$");

        var store = harness.ServerServices.GetRequiredService<IUploadTransactionStore>();
        store.TryGet(result.Value.TransactionId, out var transaction).Should().BeTrue();
        transaction!.IsComplete.Should().BeTrue();
        transaction.OrderData.Should().Equal(pain);

        // The seam no single-layer test can reach: whichever submission convention the connector chose for
        // this version (H003/H004 send OrderType="CCT" directly, H005 sends AdminOrderType="BTU" plus a
        // BTF of SCT/pain.001), the server resolves it back to the one classical code it authorises and
        // processes against.
        transaction.EffectiveOrderType.Should().Be("CCT");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task CctUpload_WithoutPermission_IsRejected(EbicsVersion version)
    {
        // Authorised for C53 only: the CCT upload must fail regardless of how this version encodes it.
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "CCTNOAUTH", permissions: [new SubscriberPermission("C53", SignatureClass.T)], ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var result = await harness.Client.Send(
            new CctUploadRequest { Pain001 = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([10.00m])) }, _ct);

        // Proves authorisation runs against the resolved classical code rather than the wire-level
        // "BTU"/"FUL" identifier.
        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthorisationOrderTypeFailed.Code);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task CctUpload_WithInvalidPain001_IsRejectedOnTheLastSegment(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(_factory, version, "CCTBAD", ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        // Break the group-level CtrlSum cross-check by editing an otherwise valid sample.
        var invalid = PainSamples.CreditTransfer([12.34m, 56.78m])
            .Replace("<CtrlSum>69.12</CtrlSum>", "<CtrlSum>999.00</CtrlSum>", StringComparison.Ordinal);
        invalid.Should().Contain("999.00", "the sample layout must still be editable this way");

        var result = await harness.Client.Send(
            new CctUploadRequest { Pain001 = Encoding.UTF8.GetBytes(invalid) }, _ct);

        // The server only validates once the last segment has arrived, so this is a transfer-phase
        // business rejection travelling back through the connector's two-phase upload loop.
        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.InvalidOrderDataFormat.Code);
    }
}
