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
/// Tests for the HCA order handlers (issue #29): the key-change order that replaces an onboarded
/// subscriber's authentication (X00x) and encryption (E00x) keys. HCA arrives as a signed
/// <c>ebicsRequest</c> whose order data is E002-encrypted for the bank's public encryption key; the
/// server decrypts it with its private key, replaces the keys and keeps the subscriber
/// <see cref="SubscriberState.Ready"/>. Exercised end-to-end through <see cref="EbicsRequestPipeline"/>.
/// </summary>
public class HcaOrderHandlerTests
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
    public async Task Hca_ReplacesAuthAndEncKeys_AndKeepsSubscriberReady(EbicsVersion version)
    {
        var (pipeline, master, keys, bankKeys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);

        // Pre-store the "old" keys so the replacement is observable.
        var keyRef = new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));
        var oldAuth = RsaKeyMaterial.Generate();
        var oldEnc = RsaKeyMaterial.Generate();
        await keys.StoreAsync(keyRef, new StoredPublicKey(oldAuth.ToPublicOnly(), KeyVersion.Create("X002")), _ct);
        await keys.StoreAsync(keyRef, new StoredPublicKey(oldEnc.ToPublicOnly(), KeyVersion.Create("E002")), _ct);

        // The server now verifies the X002 signature (#58): HCA is authenticated with the current auth key
        // being replaced, so sign with the old auth key whose public part we just stored.
        var (xml, newAuth, newEnc) = BuildHcaRequest(version, bank, signWith: oldAuth);

        var result = await pipeline.ProcessAsync(xml, _ct);

        // Answered with an ebicsResponse (000000/000000).
        var envelope = Deserialize(result);
        envelope.GetType().Name.Should().Be("EbicsResponse");
        result.Version.Should().Be(version);
        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);
        headerCode.Should().Be("000000");
        bodyCode.Should().Be("000000");

        // A key change does not move the lifecycle state.
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Ready);

        // The authentication key was replaced with the new one.
        var authStored = await keys.GetAsync(keyRef, KeyPurpose.Authentication, _ct);
        authStored.Should().NotBeNull();
        authStored!.Key.Modulus.ToArray().Should().Equal(newAuth.Modulus.ToArray());
        authStored.Key.Modulus.ToArray().Should().NotEqual(oldAuth.ToPublicOnly().Modulus.ToArray());
        authStored.Key.HasPrivateKey.Should().BeFalse();

        // The encryption key was replaced with the new one.
        var encStored = await keys.GetAsync(keyRef, KeyPurpose.Encryption, _ct);
        encStored.Should().NotBeNull();
        encStored!.Key.Modulus.ToArray().Should().Equal(newEnc.Modulus.ToArray());
        encStored.Key.Modulus.ToArray().Should().NotEqual(oldEnc.ToPublicOnly().Modulus.ToArray());
    }

    // --- Wrong subscriber state / unknown (091002) -----------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Hca_WhenSubscriberNotReady_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master, _, bankKeys) = BuildServer();
        // INI done but not HIA -> still Initialized; a key change is not yet allowed.
        await SeedSubscriberAsync(master, SubscriberState.Initialized);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        var (xml, _, _) = BuildHcaRequest(version, bank);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Initialized);
    }

    [Fact]
    public async Task Hca_ForUnknownSubscriber_ReturnsInvalidUserOrUserState()
    {
        var (pipeline, master, _, bankKeys) = BuildServer();
        await master.SaveBankAsync(new Bank(HostId.Create(Host)), _ct);
        await master.SavePartnerAsync(new Partner(HostId.Create(Host), PartnerId.Create(Partner)), _ct);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        var (xml, _, _) = BuildHcaRequest(EbicsVersion.H004, bank);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    // --- Malformed order data / bad key version (090004) -----------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Hca_WithUndecompressableOrderData_ReturnsInvalidOrderDataFormat(EbicsVersion version)
    {
        var (pipeline, master, _, bankKeys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        // Correctly E002-encrypted, but the plaintext is not a valid zlib stream -> decompression fails.
        var xml = ServerTestHelpers.BuildEncryptedRequestWithOrderData(
            version, Host, Partner, User, [1, 2, 3, 4], "HCA", bank.Encryption, bank.EncryptionVersion);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
    }

    [Fact]
    public async Task Hca_WithNonAuthenticationKeyVersion_ReturnsInvalidOrderDataFormat()
    {
        var (pipeline, master, _, bankKeys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var bank = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        // A005 is a signature version, not an authentication (X00x) version.
        var xml = ServerTestHelpers.BuildEncryptedHcaRequest(
            EbicsVersion.H004, Host, Partner, User, bank.Encryption, bank.EncryptionVersion,
            authenticationVersion: "A005", rsaAuthKey: RsaKeyMaterial.Generate(), rsaEncKey: RsaKeyMaterial.Generate());

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("090004");
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master, IServerKeyStore Keys, IServerBankKeyStore BankKeys) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>(),
            provider.GetRequiredService<IServerKeyStore>(),
            provider.GetRequiredService<IServerBankKeyStore>());
    }

    // Builds a valid HCA request for the version (order data encrypted for the bank's E002 key) and
    // returns it with the expected (public) new keys.
    private static (string Xml, RsaKeyMaterial ExpectedAuth, RsaKeyMaterial ExpectedEnc) BuildHcaRequest(
        EbicsVersion version, BankKeyPair bank, RsaKeyMaterial? signWith = null)
    {
        if (version == EbicsVersion.H005)
        {
            using X509Certificate2 authCert = TestCertificates.CreateSelfSigned();
            using X509Certificate2 encCert = TestCertificates.CreateSelfSigned();
            var expectedAuth = RsaKeyImportExport.ImportPublicKeyFromCertificate(authCert);
            var expectedEnc = RsaKeyImportExport.ImportPublicKeyFromCertificate(encCert);
            var xml = ServerTestHelpers.BuildEncryptedHcaRequest(
                version, Host, Partner, User, bank.Encryption, bank.EncryptionVersion,
                authCertificate: authCert, encCertificate: encCert, signWithAuthKey: signWith);
            return (xml, expectedAuth, expectedEnc);
        }

        var authKey = RsaKeyMaterial.Generate();
        var encKey = RsaKeyMaterial.Generate();
        var request = ServerTestHelpers.BuildEncryptedHcaRequest(
            version, Host, Partner, User, bank.Encryption, bank.EncryptionVersion,
            rsaAuthKey: authKey, rsaEncKey: encKey, signWithAuthKey: signWith);
        return (request, authKey.ToPublicOnly(), encKey.ToPublicOnly());
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
