using System.Security.Cryptography.X509Certificates;
using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using EBICO.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for the INI order handlers (issue #26): the server receives the subscriber's bank-technical
/// signature key (A00x) via an <c>ebicsUnsecuredRequest</c>, stores it and moves the subscriber
/// <c>New → Initialized</c>. Exercised end-to-end through <see cref="EbicsRequestPipeline"/> (parse →
/// dispatch → handle → respond) so the response envelope type and the state side effects are covered.
/// </summary>
public class IniOrderHandlerTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public static TheoryData<EbicsVersion> AllVersions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    // --- Happy path ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Ini_AcceptsSignatureKey_StoresIt_AndInitializesSubscriber(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.New);
        var (xml, expectedKey) = BuildIniRequest(version);

        var result = await pipeline.ProcessAsync(xml, _ct);

        // The response is an ebicsKeyManagementResponse reporting success (000000/000000).
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
        envelope.GetType().Name.Should().Be("EbicsKeyManagementResponse");
        result.Version.Should().Be(version);
        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);
        headerCode.Should().Be("000000");
        bodyCode.Should().Be("000000");

        // The subscriber advanced New -> Initialized.
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Initialized);

        // The signature key was stored and matches what was sent.
        var stored = await keys.GetAsync(new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User)), KeyPurpose.Signature, _ct);
        stored.Should().NotBeNull();
        stored!.Version.Value.Should().Be("A005");
        stored.Key.Modulus.ToArray().Should().Equal(expectedKey.Modulus.ToArray());
        stored.Key.Exponent.ToArray().Should().Equal(expectedKey.Exponent.ToArray());
        stored.Key.HasPrivateKey.Should().BeFalse();
    }

    // --- "Already initialized" and unknown user (091002) -----------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Ini_WhenAlreadyInitialized_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        var (xml, _) = BuildIniRequest(version);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");

        // No new key was stored and the state is unchanged.
        (await keys.ContainsAsync(new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User)), KeyPurpose.Signature, _ct))
            .Should().BeFalse();
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Initialized);
    }

    [Fact]
    public async Task Ini_ForUnknownSubscriber_ReturnsInvalidUserOrUserState()
    {
        var (pipeline, master, _) = BuildServer();
        // Bank + partner exist, but the subscriber was never created.
        await master.SaveBankAsync(new Bank(HostId.Create(Host)), _ct);
        await master.SavePartnerAsync(new Partner(HostId.Create(Host), PartnerId.Create(Partner)), _ct);
        var (xml, _) = BuildIniRequest(EbicsVersion.H004);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    // --- Malformed order data / bad key version (090004) -----------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Ini_WithUndecodableOrderData_ReturnsInvalidOrderDataFormat(EbicsVersion version)
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.New);
        // Not a valid zlib stream -> decompression fails.
        var xml = ServerTestHelpers.BuildUnsecuredRequestWithOrderData(version, Host, Partner, User, [1, 2, 3, 4]);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
        // The subscriber stays New on a rejected INI.
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.New);
    }

    [Fact]
    public async Task Ini_WithSignatureVersionNotPermittedForProtocol_ReturnsInvalidOrderDataFormat()
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.New);
        // A006 (PSS) is only permitted on H005, not on H004.
        var xml = ServerTestHelpers.BuildUnsecuredIniRequest(
            EbicsVersion.H004, Host, Partner, User, signatureVersion: "A006", rsaKey: RsaKeyMaterial.Generate());

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
    }

    [Fact]
    public async Task Ini_WithNonSignatureKeyVersion_ReturnsInvalidOrderDataFormat()
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.New);
        // X002 is an authentication version, not a signature (A00x) version.
        var xml = ServerTestHelpers.BuildUnsecuredIniRequest(
            EbicsVersion.H004, Host, Partner, User, signatureVersion: "X002", rsaKey: RsaKeyMaterial.Generate());

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerKeyStore Keys) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerKeyStore>());
    }

    private async Task SeedSubscriberAsync(IMasterDataManager master, SubscriberState state)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);

        if (state != SubscriberState.New)
        {
            await master.TransitionSubscriberAsync(host, partner, user, state, _ct);
        }
    }

    // Builds a valid INI request for the version and returns it with the expected (public) key.
    private static (string Xml, RsaKeyMaterial ExpectedKey) BuildIniRequest(EbicsVersion version)
    {
        if (version == EbicsVersion.H005)
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            var expected = RsaKeyImportExport.ImportPublicKeyFromCertificate(certificate);
            var xml = ServerTestHelpers.BuildUnsecuredIniRequest(version, Host, Partner, User, certificate: certificate);
            return (xml, expected);
        }

        var key = RsaKeyMaterial.Generate();
        var request = ServerTestHelpers.BuildUnsecuredIniRequest(version, Host, Partner, User, rsaKey: key);
        return (request, key.ToPublicOnly());
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
