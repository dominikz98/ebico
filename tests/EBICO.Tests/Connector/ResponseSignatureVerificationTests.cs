using System.Text;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Download;
using EBICO.Connector.Keys;
using EBICO.Connector.Transport;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.ReturnCodes;
using EBICO.Server.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EBICO.Tests.Connector;

/// <summary>
/// Tests for the connector's verification of the bank's X002 authentication signature on a transaction
/// <c>ebicsResponse</c> (issue #143) — the client-side half of what makes a return code or a downloaded
/// statement attributable to the bank rather than to whatever answered on the wire.
/// </summary>
/// <remarks>
/// Driven through the real download handler rather than the verifier in isolation: the point is that a
/// caller never receives content from an unauthenticated response, which is a property of the pipeline,
/// not of a helper. The happy path is covered by every other download/upload suite — their fake servers
/// sign as the bank does, so those suites would go red if verification stopped working.
/// </remarks>
public class ResponseSignatureVerificationTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Every supported protocol version — verification is version-agnostic and must stay so.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Unsigned_response_is_rejected(EbicsVersion version)
    {
        // What EBICO returned before #143, and what made every order after onboarding fail on a client
        // that verifies.
        using var harness = await CreateAsync(version, Responder.Unsigned(version), _ct);

        var act = async () => await harness.SendC53Async(_ct);

        (await act.Should().ThrowAsync<EbicsResponseSignatureException>())
            .Which.Message.Should().Contain("unsigned");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Response_signed_with_a_foreign_key_is_rejected(EbicsVersion version)
    {
        using var harness = await CreateAsync(version, Responder.SignedWith(version, RsaKeyMaterial.Generate()), _ct);

        var act = async () => await harness.SendC53Async(_ct);

        await act.Should().ThrowAsync<EbicsResponseSignatureException>();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Response_tampered_with_after_signing_is_rejected(EbicsVersion version)
    {
        // Models a man-in-the-middle flipping the return code of a validly signed response.
        using var harness = await CreateAsync(version, Responder.SignedThenTampered(version), _ct);

        var act = async () => await harness.SendC53Async(_ct);

        await act.Should().ThrowAsync<EbicsResponseSignatureException>();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Verification_can_be_switched_off_per_connection(EbicsVersion version)
    {
        // The escape hatch for a server that does not sign; the response is then unauthenticated and the
        // return code surfaces as an ordinary failure instead.
        using var harness = await CreateAsync(version, Responder.Unsigned(version), _ct, verifySignature: false);

        var result = await harness.SendC53Async(_ct);

        result.IsSuccess.Should().BeFalse("the canned response reports a failure return code");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Missing_bank_key_is_a_configuration_error(EbicsVersion version)
    {
        using var harness = await CreateAsync(version, Responder.Unsigned(version), _ct, storeBankAuthKey: false);

        var act = async () => await harness.SendC53Async(_ct);

        (await act.Should().ThrowAsync<EbicsConfigurationException>())
            .Which.Message.Should().Contain("HPB");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Signed_response_passes(EbicsVersion version)
    {
        using var harness = await CreateAsync(version, Responder.SignedWith(version, FakeBankIdentity.AuthenticationKeyPair), _ct);

        var result = await harness.SendC53Async(_ct);

        // A signature that verifies lets the pipeline through to the ordinary return-code handling.
        result.IsSuccess.Should().BeFalse("the canned response reports a failure return code, not a signature problem");
    }

    private static async Task<VerificationHarness> CreateAsync(
        EbicsVersion version,
        Func<EbicsHttpRequest, EbicsHttpResponse> responder,
        CancellationToken ct,
        bool verifySignature = true,
        bool storeBankAuthKey = true)
    {
        var services = new ServiceCollection();
        services.AddEbicoConnector(o =>
        {
            o.Url = "https://bank.example/ebics";
            o.HostId = "HOST";
            o.PartnerId = "PART";
            o.UserId = "USER";
            o.Version = version;
            o.VerifyResponseSignature = verifySignature;
        });
        services.AddEbicoDownload();
        services.RemoveAll<ITransport>();
        services.AddSingleton<ITransport>(new FakeTransport(responder));
        var provider = services.BuildServiceProvider();

        var keys = provider.GetRequiredService<IKeyStore>();
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Encryption, RsaKeyMaterial.Generate(), ct);
        await keys.StoreAsync(KeyOwner.Subscriber, KeyPurpose.Authentication, RsaKeyMaterial.Generate(), ct);
        if (storeBankAuthKey)
        {
            await keys.StoreAsync(KeyOwner.Bank, KeyPurpose.Authentication, FakeBankIdentity.PublicAuthenticationKey, ct);
        }

        return new VerificationHarness(provider);
    }

    /// <summary>Canned server answers covering the shapes a client must not act on.</summary>
    private static class Responder
    {
        public static Func<EbicsHttpRequest, EbicsHttpResponse> Unsigned(EbicsVersion version)
            => _ => Respond(EbicsXmlSerializer.SerializeToUtf8Bytes(BuildFailureResponse(version)));

        public static Func<EbicsHttpRequest, EbicsHttpResponse> SignedWith(EbicsVersion version, RsaKeyMaterial key)
            => _ =>
            {
                var response = (IAuthSignedResponseEnvelope)BuildFailureResponse(version);
                response.AuthSignature = AuthenticationSignature.Sign(
                    EbicsXmlSerializer.SerializeToString(response),
                    key,
                    KeyVersions.Default(KeyPurpose.Authentication, version).Version);
                return Respond(EbicsXmlSerializer.SerializeToUtf8Bytes(response));
            };

        public static Func<EbicsHttpRequest, EbicsHttpResponse> SignedThenTampered(EbicsVersion version)
            => request =>
            {
                var signed = SignedWith(version, FakeBankIdentity.AuthenticationKeyPair)(request);
                var original = Encoding.UTF8.GetString(signed.Payload.Span);

                // Turn the bank's rejection into an acceptance — the edit a man-in-the-middle would make.
                var tampered = original.Replace(
                    EbicsReturnCode.MaxSegmentsExceeded.Code, EbicsReturnCode.Ok.Code, StringComparison.Ordinal);
                if (tampered == original)
                {
                    // A silent no-op here would make the test pass for the wrong reason: nothing was
                    // altered, so of course the signature still verifies.
                    throw new InvalidOperationException(
                        "The tampering fixture changed nothing — the response no longer carries the expected return code.");
                }

                return Respond(Encoding.UTF8.GetBytes(tampered));
            };

        // A failure return code keeps the fixture small: the download never gets past initialisation, so
        // no encryption material has to be assembled, and every assertion here is about the signature.
        private static IEbicsResponseEnvelope BuildFailureResponse(EbicsVersion version)
            => new EbicsResponseFactory().BuildDownloadResponse(
                version,
                new DownloadTransactionResult(
                    EbicsReturnCode.MaxSegmentsExceeded, EbicsTransactionPhase.Initialisation));

        private static EbicsHttpResponse Respond(byte[] payload)
            => new() { StatusCode = 200, Payload = payload };
    }

    private sealed class VerificationHarness(ServiceProvider provider) : IDisposable
    {
        public Task<EbicsResult<DownloadResult>> SendC53Async(CancellationToken ct)
            => provider.GetRequiredService<IEbicsClient>().Send(new C53DownloadRequest(), ct);

        public void Dispose() => provider.Dispose();
    }
}
