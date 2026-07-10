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
/// Tests for the HIA order handlers (issue #27): the server receives the subscriber's authentication
/// key (X00x) and encryption key (E00x) via an <c>ebicsUnsecuredRequest</c>, stores both and moves the
/// subscriber <c>Initialized → Ready</c>. Exercised end-to-end through <see cref="EbicsRequestPipeline"/>
/// (parse → dispatch → handle → respond) so the response envelope type and the state side effects are covered.
/// </summary>
public class HiaOrderHandlerTests
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
    public async Task Hia_AcceptsAuthAndEncKeys_StoresThem_AndMakesSubscriberReady(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        var (xml, expectedAuth, expectedEnc) = BuildHiaRequest(version);

        var result = await pipeline.ProcessAsync(xml, _ct);

        // The response is an ebicsKeyManagementResponse reporting success (000000/000000).
        var envelope = EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
        envelope.GetType().Name.Should().Be("EbicsKeyManagementResponse");
        result.Version.Should().Be(version);
        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);
        headerCode.Should().Be("000000");
        bodyCode.Should().Be("000000");

        // The subscriber advanced Initialized -> Ready.
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Ready);

        var keyRef = new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));

        // The authentication key (X002) was stored and matches what was sent.
        var authStored = await keys.GetAsync(keyRef, KeyPurpose.Authentication, _ct);
        authStored.Should().NotBeNull();
        authStored!.Version.Value.Should().Be("X002");
        authStored.Key.Modulus.ToArray().Should().Equal(expectedAuth.Modulus.ToArray());
        authStored.Key.Exponent.ToArray().Should().Equal(expectedAuth.Exponent.ToArray());
        authStored.Key.HasPrivateKey.Should().BeFalse();

        // The encryption key (E002) was stored and matches what was sent.
        var encStored = await keys.GetAsync(keyRef, KeyPurpose.Encryption, _ct);
        encStored.Should().NotBeNull();
        encStored!.Version.Value.Should().Be("E002");
        encStored.Key.Modulus.ToArray().Should().Equal(expectedEnc.Modulus.ToArray());
        encStored.Key.Exponent.ToArray().Should().Equal(expectedEnc.Exponent.ToArray());
        encStored.Key.HasPrivateKey.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task IniThenHia_OnboardsSubscriber_AndAllThreeKeysCoexist(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.New);

        // INI first: bank-technical signature key, New -> Initialized.
        var iniResult = await pipeline.ProcessAsync(BuildIniRequestXml(version), _ct);
        ServerTestHelpers.ReadReturnCodes(Deserialize(iniResult)).BodyCode.Should().Be("000000");
        var afterIni = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        afterIni!.State.Should().Be(SubscriberState.Initialized);

        // HIA next: authentication + encryption keys, Initialized -> Ready.
        var (hiaXml, _, _) = BuildHiaRequest(version);
        var hiaResult = await pipeline.ProcessAsync(hiaXml, _ct);
        ServerTestHelpers.ReadReturnCodes(Deserialize(hiaResult)).BodyCode.Should().Be("000000");
        var afterHia = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        afterHia!.State.Should().Be(SubscriberState.Ready);

        // All three keys coexist purpose-isolated for the same subscriber.
        var keyRef = new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));
        (await keys.ContainsAsync(keyRef, KeyPurpose.Signature, _ct)).Should().BeTrue();
        (await keys.ContainsAsync(keyRef, KeyPurpose.Authentication, _ct)).Should().BeTrue();
        (await keys.ContainsAsync(keyRef, KeyPurpose.Encryption, _ct)).Should().BeTrue();
    }

    // --- INI not yet run / unknown / already past HIA (091002) -----------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Hia_WhenSubscriberStillNew_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        // INI has not run yet -> the subscriber is still New; HIA-before-INI is rejected.
        await SeedSubscriberAsync(master, SubscriberState.New);
        var (xml, _, _) = BuildHiaRequest(version);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");

        // No keys were stored and the state is unchanged.
        var keyRef = new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));
        (await keys.ContainsAsync(keyRef, KeyPurpose.Authentication, _ct)).Should().BeFalse();
        (await keys.ContainsAsync(keyRef, KeyPurpose.Encryption, _ct)).Should().BeFalse();
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.New);
    }

    [Fact]
    public async Task Hia_ForUnknownSubscriber_ReturnsInvalidUserOrUserState()
    {
        var (pipeline, master, _) = BuildServer();
        // Bank + partner exist, but the subscriber was never created.
        await master.SaveBankAsync(new Bank(HostId.Create(Host)), _ct);
        await master.SavePartnerAsync(new Partner(HostId.Create(Host), PartnerId.Create(Partner)), _ct);
        var (xml, _, _) = BuildHiaRequest(EbicsVersion.H004);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Hia_WhenAlreadyReady_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, _) = BuildServer();
        // Re-HIA on an already-onboarded subscriber is rejected.
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var (xml, _, _) = BuildHiaRequest(version);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    // --- Malformed order data / bad key version (090004) -----------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Hia_WithUndecodableOrderData_ReturnsInvalidOrderDataFormat(EbicsVersion version)
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        // Not a valid zlib stream -> decompression fails.
        var xml = ServerTestHelpers.BuildUnsecuredRequestWithOrderData(version, Host, Partner, User, [1, 2, 3, 4], "HIA");

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
        // The subscriber stays Initialized on a rejected HIA.
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Initialized);
    }

    [Fact]
    public async Task Hia_WithEncryptionVersionNotPermittedForProtocol_ReturnsInvalidOrderDataFormat()
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        // E001 (legacy) is only permitted on H003/H004, not on H005.
        using X509Certificate2 authCert = TestCertificates.CreateSelfSigned();
        using X509Certificate2 encCert = TestCertificates.CreateSelfSigned();
        var xml = ServerTestHelpers.BuildUnsecuredHiaRequest(
            EbicsVersion.H005, Host, Partner, User, encryptionVersion: "E001",
            authCertificate: authCert, encCertificate: encCert);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
    }

    [Fact]
    public async Task Hia_WithAuthenticationVersionNotPermittedForProtocol_ReturnsInvalidOrderDataFormat()
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        // X001 (legacy) is only permitted on H003/H004, not on H005.
        using X509Certificate2 authCert = TestCertificates.CreateSelfSigned();
        using X509Certificate2 encCert = TestCertificates.CreateSelfSigned();
        var xml = ServerTestHelpers.BuildUnsecuredHiaRequest(
            EbicsVersion.H005, Host, Partner, User, authenticationVersion: "X001",
            authCertificate: authCert, encCertificate: encCert);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
    }

    [Fact]
    public async Task Hia_WithNonAuthenticationKeyVersion_ReturnsInvalidOrderDataFormat()
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        // A005 is a signature version, not an authentication (X00x) version.
        var xml = ServerTestHelpers.BuildUnsecuredHiaRequest(
            EbicsVersion.H004, Host, Partner, User, authenticationVersion: "A005",
            rsaAuthKey: RsaKeyMaterial.Generate(), rsaEncKey: RsaKeyMaterial.Generate());

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

        // Advance one legal step at a time: New -> Initialized (INI) -> Ready.
        if (state is SubscriberState.Initialized or SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        }

        if (state == SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
        }
    }

    // Builds a valid INI request (signature key) for the version.
    private static string BuildIniRequestXml(EbicsVersion version)
    {
        if (version == EbicsVersion.H005)
        {
            using X509Certificate2 certificate = TestCertificates.CreateSelfSigned();
            return ServerTestHelpers.BuildUnsecuredIniRequest(version, Host, Partner, User, certificate: certificate);
        }

        return ServerTestHelpers.BuildUnsecuredIniRequest(version, Host, Partner, User, rsaKey: RsaKeyMaterial.Generate());
    }

    // Builds a valid HIA request for the version and returns it with the expected (public) keys.
    private static (string Xml, RsaKeyMaterial ExpectedAuth, RsaKeyMaterial ExpectedEnc) BuildHiaRequest(EbicsVersion version)
    {
        if (version == EbicsVersion.H005)
        {
            using X509Certificate2 authCert = TestCertificates.CreateSelfSigned();
            using X509Certificate2 encCert = TestCertificates.CreateSelfSigned();
            var expectedAuth = RsaKeyImportExport.ImportPublicKeyFromCertificate(authCert);
            var expectedEnc = RsaKeyImportExport.ImportPublicKeyFromCertificate(encCert);
            var xml = ServerTestHelpers.BuildUnsecuredHiaRequest(version, Host, Partner, User, authCertificate: authCert, encCertificate: encCert);
            return (xml, expectedAuth, expectedEnc);
        }

        var authKey = RsaKeyMaterial.Generate();
        var encKey = RsaKeyMaterial.Generate();
        var request = ServerTestHelpers.BuildUnsecuredHiaRequest(version, Host, Partner, User, rsaAuthKey: authKey, rsaEncKey: encKey);
        return (request, authKey.ToPublicOnly(), encKey.ToPublicOnly());
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
