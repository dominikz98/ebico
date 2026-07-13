using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for the HSA order handlers (issue #29): the legacy combined key-initialisation order that
/// carries the subscriber's authentication (X00x) and encryption (E00x) keys via an
/// <c>ebicsUnsecuredRequest</c> and moves the subscriber <c>Initialized → Ready</c>. HSA exists only
/// for H003/H004. Exercised end-to-end through <see cref="EbicsRequestPipeline"/>.
/// </summary>
public class HsaOrderHandlerTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    // HSA was removed in H005; only H003/H004 have an HSARequestOrderData.
    public static TheoryData<EbicsVersion> HsaVersions => [EbicsVersion.H003, EbicsVersion.H004];

    // --- Happy path ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(HsaVersions))]
    public async Task Hsa_AcceptsAuthAndEncKeys_StoresThem_AndMakesSubscriberReady(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        var authKey = RsaKeyMaterial.Generate();
        var encKey = RsaKeyMaterial.Generate();
        var xml = ServerTestHelpers.BuildUnsecuredHsaRequest(version, Host, Partner, User, rsaAuthKey: authKey, rsaEncKey: encKey);

        var result = await pipeline.ProcessAsync(xml, _ct);

        // HSA is an unsecured request, so it is answered with an ebicsKeyManagementResponse (000000/000000).
        var envelope = Deserialize(result);
        envelope.GetType().Name.Should().Be("EbicsKeyManagementResponse");
        result.Version.Should().Be(version);
        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);
        headerCode.Should().Be("000000");
        bodyCode.Should().Be("000000");

        // The subscriber advanced Initialized -> Ready.
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Ready);

        var keyRef = new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));
        var authStored = await keys.GetAsync(keyRef, KeyPurpose.Authentication, _ct);
        authStored.Should().NotBeNull();
        authStored!.Version.Value.Should().Be("X002");
        authStored.Key.Modulus.ToArray().Should().Equal(authKey.ToPublicOnly().Modulus.ToArray());
        authStored.Key.HasPrivateKey.Should().BeFalse();

        var encStored = await keys.GetAsync(keyRef, KeyPurpose.Encryption, _ct);
        encStored.Should().NotBeNull();
        encStored!.Version.Value.Should().Be("E002");
        encStored.Key.Modulus.ToArray().Should().Equal(encKey.ToPublicOnly().Modulus.ToArray());
        encStored.Key.HasPrivateKey.Should().BeFalse();
    }

    // --- INI not yet run / unknown / already Ready (091002) --------------------------------

    [Theory]
    [MemberData(nameof(HsaVersions))]
    public async Task Hsa_WhenSubscriberStillNew_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, keys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.New);
        var xml = ServerTestHelpers.BuildUnsecuredHsaRequest(
            version, Host, Partner, User, rsaAuthKey: RsaKeyMaterial.Generate(), rsaEncKey: RsaKeyMaterial.Generate());

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
        var keyRef = new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));
        (await keys.ContainsAsync(keyRef, KeyPurpose.Authentication, _ct)).Should().BeFalse();
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.New);
    }

    [Fact]
    public async Task Hsa_ForUnknownSubscriber_ReturnsInvalidUserOrUserState()
    {
        var (pipeline, master, _) = BuildServer();
        await master.SaveBankAsync(new Bank(HostId.Create(Host)), _ct);
        await master.SavePartnerAsync(new Partner(HostId.Create(Host), PartnerId.Create(Partner)), _ct);
        var xml = ServerTestHelpers.BuildUnsecuredHsaRequest(
            EbicsVersion.H004, Host, Partner, User, rsaAuthKey: RsaKeyMaterial.Generate(), rsaEncKey: RsaKeyMaterial.Generate());

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    [Theory]
    [MemberData(nameof(HsaVersions))]
    public async Task Hsa_WhenAlreadyReady_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var xml = ServerTestHelpers.BuildUnsecuredHsaRequest(
            version, Host, Partner, User, rsaAuthKey: RsaKeyMaterial.Generate(), rsaEncKey: RsaKeyMaterial.Generate());

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    // --- Malformed order data / bad key version (090004) -----------------------------------

    [Theory]
    [MemberData(nameof(HsaVersions))]
    public async Task Hsa_WithUndecodableOrderData_ReturnsInvalidOrderDataFormat(EbicsVersion version)
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        // Not a valid zlib stream -> decompression fails.
        var xml = ServerTestHelpers.BuildUnsecuredRequestWithOrderData(version, Host, Partner, User, [1, 2, 3, 4], "HSA");

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Initialized);
    }

    [Fact]
    public async Task Hsa_WithNonEncryptionKeyVersion_ReturnsInvalidOrderDataFormat()
    {
        var (pipeline, master, _) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        // A005 is a signature version, not an encryption (E00x) version.
        var xml = ServerTestHelpers.BuildUnsecuredHsaRequest(
            EbicsVersion.H004, Host, Partner, User, encryptionVersion: "A005",
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

        if (state is SubscriberState.Initialized or SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        }

        if (state == SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
        }
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
