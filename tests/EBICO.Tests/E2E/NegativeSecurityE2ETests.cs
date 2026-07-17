extern alias EbicoServer;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using EBICO.Connector.Upload;
using EBICO.Core;
using EBICO.Core.ReturnCodes;
using EBICO.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EBICO.Tests.E2E;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// End-to-end negative/security cases (issue #58). With the server now verifying the X002 authentication
/// signature (<c>X002EbicsRequestVerifier</c>), the real connector drives a real upload against the
/// in-process server while a <see cref="RequestTamperingHandler"/> corrupts the already-signed request on
/// the wire. Two facts are proven:
/// <list type="bullet">
/// <item>The request header (and the crypto metadata) is <c>authenticate="true"</c>, so tampering it — or
/// the signature value itself — is rejected with <c>EBICS_AUTHENTICATION_FAILED</c> (061001). This is what
/// protects the segment metadata (<c>NumSegments</c>, transaction id, segment numbers) on the wire.</item>
/// <item>The <c>OrderData</c> payload is <b>not</b> authenticated, so corrupting the ciphertext survives
/// signature verification and is caught later on decryption as <c>EBICS_INVALID_ORDER_DATA_FORMAT</c>
/// (090004).</item>
/// </list>
/// The classical segment-inconsistency return codes (duplicate 091103, underrun 011101, unknown txid
/// 091101, segment beyond count 091104) require a validly-signed but logically-inconsistent request, which
/// the connector never produces; they are covered at the server-pipeline layer
/// (<c>Server/UploadTransactionTests</c>, <c>Server/DownloadTransactionTests</c>).
/// </summary>
public class NegativeSecurityE2ETests : IClassFixture<WebApplicationFactory<ServerProgram>>
{
    private readonly WebApplicationFactory<ServerProgram> _factory;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Initializes the test with the shared web-application factory.</summary>
    /// <param name="factory">The application factory fixture.</param>
    public NegativeSecurityE2ETests(WebApplicationFactory<ServerProgram> factory) => _factory = factory;

    /// <summary>The EBICS versions covered by the end-to-end matrix.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Upload_WithTamperedAuthSignature_IsRejected(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "SIGTAMPER", installTamperingHandler: true, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        // Corrupt the RSA signature value on the (signed) initialisation request: it no longer verifies
        // against the subscriber's authentication key delivered during HIA.
        harness.Tampering!.Tamper = xml => FlipBase64Element(xml, "SignatureValue");

        var result = await harness.Client.Send(UploadRequest(), _ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthenticationFailed.Code);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Upload_WithTamperedSegmentCountInSignedHeader_IsRejected(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "HDRTAMPER", installTamperingHandler: true, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        // NumSegments lives in the authenticated static header. Rewriting it on the wire breaks the
        // reference digest — proof that X002 protects the segment metadata against tampering.
        harness.Tampering!.Tamper = xml => ReplaceElementValue(xml, "NumSegments", "1", "2");

        var result = await harness.Client.Send(UploadRequest(), _ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.AuthenticationFailed.Code);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Upload_WithTamperedOrderData_IsRejected(EbicsVersion version)
    {
        await using var harness = await EbicsE2EHarness.CreateAsync(
            _factory, version, "DATATAMPER", installTamperingHandler: true, ct: _ct);
        (await harness.OnboardAsync(_ct)).ThrowIfFailed();

        // OrderData is not in the authenticated node-set, so corrupting the ciphertext survives signature
        // verification; the server then fails to decrypt/decompress the reassembled order data. Only the
        // transfer request carries OrderData — the initialisation passes through untouched and verifies.
        harness.Tampering!.Tamper = xml => xml.Contains("<OrderData", StringComparison.Ordinal)
            ? FlipBase64Element(xml, "OrderData")
            : xml;

        var result = await harness.Client.Send(UploadRequest(), _ct);

        result.IsSuccess.Should().BeFalse();
        result.ReturnCode.Should().Be(EbicsReturnCode.InvalidOrderDataFormat.Code);
    }

    private static CctUploadRequest UploadRequest()
        => new() { Pain001 = Encoding.UTF8.GetBytes(PainSamples.CreditTransfer([12.34m, 56.78m])) };

    // Flips the first character of a base64 element's content to a different valid base64 character,
    // yielding different bytes of the same length. Matches the element regardless of any namespace prefix.
    private static string FlipBase64Element(string xml, string localName)
    {
        var pattern = $"(<(?:\\w+:)?{localName}(?:\\s[^>]*)?>)([^<]+)(</(?:\\w+:)?{localName}>)";
        return Regex.Replace(
            xml,
            pattern,
            m =>
            {
                var content = m.Groups[2].Value;
                var flipped = (content[0] == 'A' ? 'B' : 'A') + content[1..];
                return m.Groups[1].Value + flipped + m.Groups[3].Value;
            },
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
    }

    // Replaces the exact text content of an element (regardless of any namespace prefix).
    private static string ReplaceElementValue(string xml, string localName, string from, string to)
    {
        var pattern = $"(<(?:\\w+:)?{localName}(?:\\s[^>]*)?>){Regex.Escape(from)}(</(?:\\w+:)?{localName}>)";
        return Regex.Replace(
            xml,
            pattern,
            m => m.Groups[1].Value + to + m.Groups[2].Value,
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
    }
}
