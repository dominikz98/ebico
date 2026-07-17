using AwesomeAssertions;
using EBICO.Connector.Quickstart;
using EBICO.Core.ReturnCodes;

namespace EBICO.Tests.Packaging;

/// <summary>
/// Smoke test for the quickstart sample (issue #50): runs <see cref="QuickstartRunner.RunAsync"/>, which
/// hosts the EBICO.Server in-process and drives the connector through key generation, onboarding
/// (INI/HIA/HPB), a CCT upload and a C53 download. Proves the documented quickstart actually works
/// end-to-end, so a regression in the sample or the packaged API surface fails the build.
/// </summary>
public sealed class QuickstartSampleTests
{
    [Fact]
    public async Task Quickstart_runs_full_round_trip_successfully()
    {
        var result = await QuickstartRunner.RunAsync(TextWriter.Null, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.IniReturnCode.Should().Be(EbicsReturnCode.OkCode);
        result.HiaReturnCode.Should().Be(EbicsReturnCode.OkCode);
        result.HpbReturnCode.Should().Be(EbicsReturnCode.OkCode);
        result.UploadReturnCode.Should().Be(EbicsReturnCode.OkCode);
        result.UploadTransactionId.Should().NotBeNullOrEmpty();
        // Ein erfolgreicher Download endet mit 011000 (EBICS_DOWNLOAD_POSTPROCESS_DONE), nicht 000000.
        result.DownloadReturnCode.Should().Be(EbicsReturnCode.DownloadPostprocessDone.Code);
        result.DownloadSegments.Should().BeGreaterThan(0);
    }
}
