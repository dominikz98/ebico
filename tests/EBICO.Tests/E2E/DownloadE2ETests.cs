extern alias EbicoServer;
using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using EBICO.Connector.Download;
using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EBICO.Tests.E2E;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end camt.053 statement download (issue #57): the real connector runs the three-phase download
/// (initialisation → transfer → receipt) against the real in-process server, which generates a synthetic
/// statement on demand, compresses, E002-encrypts it for the subscriber key delivered during HIA and
/// segments it. The connector's pipeline reverses all of that before the receipt goes out.
/// </summary>
public class DownloadE2ETests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public DownloadE2ETests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>The EBICS versions covered by the end-to-end matrix.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task C53Download_RoundTrips_AndYieldsCamt053InZip(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(_factory, version, "C53", ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        // The parse hook runs on the decrypted order data before the receipt is sent, so a successful
        // unzip here proves the payload was intact at the moment the connector acknowledged it.
        var result = await harness.Client.Send(new C53DownloadRequest { Parse = zip => Unzip(zip.ToArray()) }, _ct);

        result.IsSuccess.Should().BeTrue($"C53 download failed: {result.ReturnCode} {result.ReturnText}");

        // Not "000000": the positive receipt's own code wins when the download's return codes are
        // combined, so a successful three-phase download reports EBICS_DOWNLOAD_POSTPROCESS_DONE.
        result.ReturnCode.Should().Be(EbicsReturnCode.DownloadPostprocessDone.Code);
        result.Value!.NumSegments.Should().BeGreaterThanOrEqualTo(1);
        result.Value.TransactionId.Should().MatchRegex("^[0-9A-F]{32}$");

        var camt = Encoding.UTF8.GetString(result.Value.ParsedAs<byte[]>()!);
        camt.Should().Contain("camt.053.001.08").And.Contain("BkToCstmrStmt");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task C53Download_WithoutPermission_IsRejected(EbicsVersion version)
    {
        // Authorised for CCT only: the C53 download must fail regardless of how this version encodes it.
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "C53NOAUTH", permissions: [new SubscriberPermission("CCT", SignatureClass.T)], ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        var result = await harness.Client.Send(new C53DownloadRequest(), _ct);

        // The server rejects during the initialisation phase, before any data is dequeued, and authorises
        // against the resolved classical code (C53) rather than the wire-level H003/H004 FDL+FileFormat or
        // H005 BTF (BTD) identifier the connector actually sent.
        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthorisationOrderTypeFailed.Code);
    }

    private static byte[] Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var stream = archive.Entries.Single().Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
