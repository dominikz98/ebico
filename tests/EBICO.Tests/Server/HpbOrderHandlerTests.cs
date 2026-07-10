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
using Ds = EBICO.Core.Schema.XmlDsig;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for the HPB order handlers (issue #28): an onboarded (<see cref="SubscriberState.Ready"/>)
/// subscriber fetches the bank's public authentication (X00x) and encryption (E00x) keys via a
/// <c>ebicsNoPubKeyDigestsRequest</c>; the server answers with an <c>ebicsKeyManagementResponse</c>
/// whose <c>DataTransfer</c> carries the E002-encrypted, compressed <c>HPBResponseOrderData</c>.
/// Exercised end-to-end through <see cref="EbicsRequestPipeline"/> and proven by actually decrypting
/// the response with the subscriber's private encryption key.
/// </summary>
public class HpbOrderHandlerTests
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
    public async Task Hpb_ForReadySubscriber_ReturnsEncryptedBankKeys(EbicsVersion version)
    {
        var (pipeline, master, keys, bankKeys) = BuildServer();

        // A Ready subscriber whose encryption key is on file; we keep the private part to decrypt.
        var subscriberEnc = RsaKeyMaterial.Generate();
        await SeedReadySubscriberAsync(master, keys, subscriberEnc.ToPublicOnly());

        var xml = ServerTestHelpers.BuildNoPubKeyDigestsHpbRequest(version, Host, Partner, User);
        var result = await pipeline.ProcessAsync(xml, _ct);

        // The response is an ebicsKeyManagementResponse reporting success (000000/000000).
        var envelope = Deserialize(result);
        envelope.GetType().Name.Should().Be("EbicsKeyManagementResponse");
        result.Version.Should().Be(version);
        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);
        headerCode.Should().Be("000000");
        bodyCode.Should().Be("000000");

        // Decrypt the DataTransfer with the subscriber's private encryption key, then decompress.
        var transfer = ReadDataTransfer(version, envelope);
        var decrypted = EncryptionE002.Decrypt(
            new EncryptedOrderData(transfer.TransactionKey, transfer.OrderData), subscriberEnc, KeyVersion.Create("E002"));
        var orderDataXml = EbicsCompression.Decompress(decrypted);

        // The bank's public keys in the order data are the ones the bank key store holds.
        var expected = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        var (auth, enc) = ReadBankKeys(version, orderDataXml);
        auth.Modulus.ToArray().Should().Equal(expected.Authentication.Modulus.ToArray());
        auth.Exponent.ToArray().Should().Equal(expected.Authentication.Exponent.ToArray());
        enc.Modulus.ToArray().Should().Equal(expected.Encryption.Modulus.ToArray());
        enc.Exponent.ToArray().Should().Equal(expected.Encryption.Exponent.ToArray());

        // The DataEncryptionInfo digest identifies the subscriber's (recipient's) encryption key.
        transfer.DigestVersion.Should().Be("E002");
        transfer.Digest.Should().Equal(PublicKeyFingerprint.Compute(subscriberEnc.ToPublicOnly()));

        // The subscriber stays Ready (HPB is read-only).
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Ready);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Hpb_RepeatedCalls_ReturnTheSameBankKeys(EbicsVersion version)
    {
        var (pipeline, master, keys, _) = BuildServer();
        var subscriberEnc = RsaKeyMaterial.Generate();
        await SeedReadySubscriberAsync(master, keys, subscriberEnc.ToPublicOnly());
        var xml = ServerTestHelpers.BuildNoPubKeyDigestsHpbRequest(version, Host, Partner, User);

        var first = ReadBankKeys(version, Decompress(version, subscriberEnc, await pipeline.ProcessAsync(xml, _ct)));
        var second = ReadBankKeys(version, Decompress(version, subscriberEnc, await pipeline.ProcessAsync(xml, _ct)));

        // The bank key pair is generated once per host and cached, so HPB is stable across calls.
        second.Auth.Modulus.ToArray().Should().Equal(first.Auth.Modulus.ToArray());
        second.Enc.Modulus.ToArray().Should().Equal(first.Enc.Modulus.ToArray());
    }

    // --- End-to-end onboarding -------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task IniThenHiaThenHpb_ReturnsBankKeysDecryptableWithHiaEncryptionKey(EbicsVersion version)
    {
        var (pipeline, master, _, bankKeys) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.New);

        // INI (New -> Initialized), then HIA with a known encryption key pair (private part kept).
        await pipeline.ProcessAsync(BuildIniRequestXml(version), _ct);
        var encKeyPair = RsaKeyMaterial.Generate();
        await pipeline.ProcessAsync(BuildHiaRequestXml(version, encKeyPair), _ct);

        var afterHia = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        afterHia!.State.Should().Be(SubscriberState.Ready);

        // HPB: the response must be decryptable with the encryption key submitted during HIA.
        var result = await pipeline.ProcessAsync(ServerTestHelpers.BuildNoPubKeyDigestsHpbRequest(version, Host, Partner, User), _ct);
        var envelope = Deserialize(result);
        ServerTestHelpers.ReadReturnCodes(envelope).BodyCode.Should().Be("000000");

        var orderDataXml = Decompress(version, encKeyPair, result);
        var (auth, enc) = ReadBankKeys(version, orderDataXml);
        var expected = await bankKeys.GetOrCreateAsync(HostId.Create(Host), _ct);
        auth.Modulus.ToArray().Should().Equal(expected.Authentication.Modulus.ToArray());
        enc.Modulus.ToArray().Should().Equal(expected.Encryption.Modulus.ToArray());
    }

    // --- Subscriber not Ready / unknown / no key (091002) ----------------------------------

    [Theory]
    [MemberData(nameof(NotReadyStates))]
    public async Task Hpb_WhenSubscriberNotReady_ReturnsInvalidUserOrUserState(SubscriberState state)
    {
        var (pipeline, master, _, _) = BuildServer();
        // INI/HIA not (fully) run yet -> HPB is rejected.
        await SeedSubscriberAsync(master, state);
        var xml = ServerTestHelpers.BuildNoPubKeyDigestsHpbRequest(EbicsVersion.H004, Host, Partner, User);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    public static TheoryData<SubscriberState> NotReadyStates =>
        [SubscriberState.New, SubscriberState.Initialized];

    [Fact]
    public async Task Hpb_ForUnknownSubscriber_ReturnsInvalidUserOrUserState()
    {
        var (pipeline, master, _, _) = BuildServer();
        // Bank + partner exist, but the subscriber was never created.
        await master.SaveBankAsync(new Bank(HostId.Create(Host)), _ct);
        await master.SavePartnerAsync(new Partner(HostId.Create(Host), PartnerId.Create(Partner)), _ct);
        var xml = ServerTestHelpers.BuildNoPubKeyDigestsHpbRequest(EbicsVersion.H004, Host, Partner, User);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    [Fact]
    public async Task Hpb_WhenReadyButNoEncryptionKeyStored_ReturnsInvalidUserOrUserState()
    {
        var (pipeline, master, _, _) = BuildServer();
        // Ready subscriber but no encryption key on file (inconsistent state): HPB cannot encrypt.
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var xml = ServerTestHelpers.BuildNoPubKeyDigestsHpbRequest(EbicsVersion.H004, Host, Partner, User);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
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

    private async Task SeedSubscriberAsync(IMasterDataManager master, SubscriberState state)
    {
        var host = HostId.Create(Host);
        var partner = PartnerId.Create(Partner);
        var user = UserId.Create(User);

        await master.SaveBankAsync(new Bank(host), _ct);
        await master.SavePartnerAsync(new Partner(host, partner), _ct);
        await master.SaveSubscriberAsync(new Subscriber(host, partner, user), _ct);

        // Advance one legal step at a time: New -> Initialized (INI) -> Ready (HIA).
        if (state is SubscriberState.Initialized or SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Initialized, _ct);
        }

        if (state == SubscriberState.Ready)
        {
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Ready, _ct);
        }
    }

    private async Task SeedReadySubscriberAsync(IMasterDataManager master, IServerKeyStore keys, RsaKeyMaterial encryptionPublicKey)
    {
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var keyRef = new SubscriberKeyRef(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User));
        await keys.StoreAsync(keyRef, new StoredPublicKey(encryptionPublicKey, KeyVersion.Create("E002")), _ct);
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

    // Builds a valid HIA request whose encryption key is the given (known) pair, so the HPB response
    // can later be decrypted with its private part.
    private static string BuildHiaRequestXml(EbicsVersion version, RsaKeyMaterial encKeyPair)
    {
        if (version == EbicsVersion.H005)
        {
            using X509Certificate2 authCert = TestCertificates.CreateSelfSigned();
            using X509Certificate2 encCert = SelfSignedCertificateFactory.Create(
                encKeyPair, KeyPurpose.Encryption, "CN=EBICO Test", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
            return ServerTestHelpers.BuildUnsecuredHiaRequest(version, Host, Partner, User, authCertificate: authCert, encCertificate: encCert);
        }

        return ServerTestHelpers.BuildUnsecuredHiaRequest(version, Host, Partner, User, rsaAuthKey: RsaKeyMaterial.Generate(), rsaEncKey: encKeyPair);
    }

    // Decrypts + decompresses the HPB response's DataTransfer with the subscriber's private key.
    private static byte[] Decompress(EbicsVersion version, RsaKeyMaterial subscriberEnc, EbicsPipelineResult result)
    {
        var transfer = ReadDataTransfer(version, Deserialize(result));
        var decrypted = EncryptionE002.Decrypt(
            new EncryptedOrderData(transfer.TransactionKey, transfer.OrderData), subscriberEnc, KeyVersion.Create("E002"));
        return EbicsCompression.Decompress(decrypted);
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));

    // Reads the encrypted transaction key, encrypted order data and the recipient key digest out of a
    // key-management response's DataTransfer.
    private static (byte[] TransactionKey, byte[] OrderData, string DigestVersion, byte[] Digest) ReadDataTransfer(
        EbicsVersion version, IEbicsEnvelope envelope)
    {
        switch (version)
        {
            case EbicsVersion.H003:
                var t3 = ((H003.EbicsKeyManagementResponse)envelope).Body!.DataTransfer!;
                return (t3.DataEncryptionInfo!.TransactionKey!, t3.OrderData!.Value!, t3.DataEncryptionInfo.EncryptionPubKeyDigest!.Version!, t3.DataEncryptionInfo.EncryptionPubKeyDigest.Value!);
            case EbicsVersion.H004:
                var t4 = ((H004.EbicsKeyManagementResponse)envelope).Body!.DataTransfer!;
                return (t4.DataEncryptionInfo!.TransactionKey!, t4.OrderData!.Value!, t4.DataEncryptionInfo.EncryptionPubKeyDigest!.Version!, t4.DataEncryptionInfo.EncryptionPubKeyDigest.Value!);
            default:
                var t5 = ((H005.EbicsKeyManagementResponse)envelope).Body!.DataTransfer!;
                return (t5.DataEncryptionInfo!.TransactionKey!, t5.OrderData!.Value!, t5.DataEncryptionInfo.EncryptionPubKeyDigest!.Version!, t5.DataEncryptionInfo.EncryptionPubKeyDigest.Value!);
        }
    }

    // Deserializes the HPBResponseOrderData and extracts the bank's authentication and encryption keys.
    private static (RsaKeyMaterial Auth, RsaKeyMaterial Enc) ReadBankKeys(EbicsVersion version, byte[] orderDataXml)
    {
        var xml = Encoding.UTF8.GetString(orderDataXml);
        switch (version)
        {
            case EbicsVersion.H005:
                var od5 = EbicsXmlSerializer.Deserialize<H005.HpbResponseOrderDataType>(xml);
                return (FromCertificate(od5.AuthenticationPubKeyInfo!.X509Data!), FromCertificate(od5.EncryptionPubKeyInfo!.X509Data!));
            case EbicsVersion.H004:
                var od4 = EbicsXmlSerializer.Deserialize<H004.HpbResponseOrderDataType>(xml);
                return (FromRsaKeyValue(od4.AuthenticationPubKeyInfo!.PubKeyValue!.RsaKeyValue!), FromRsaKeyValue(od4.EncryptionPubKeyInfo!.PubKeyValue!.RsaKeyValue!));
            default:
                var od3 = EbicsXmlSerializer.Deserialize<H003.HpbResponseOrderDataType>(xml);
                return (FromRsaKeyValue(od3.AuthenticationPubKeyInfo!.PubKeyValue!.RsaKeyValue!), FromRsaKeyValue(od3.EncryptionPubKeyInfo!.PubKeyValue!.RsaKeyValue!));
        }
    }

    private static RsaKeyMaterial FromRsaKeyValue(Ds.RsaKeyValueType rsaKeyValue)
        => RsaKeyImportExport.ImportRsaKeyValue(rsaKeyValue.Modulus, rsaKeyValue.Exponent);

    private static RsaKeyMaterial FromCertificate(Ds.X509DataType x509Data)
    {
        using var cert = X509CertificateLoader.LoadCertificate(x509Data.X509Certificate[0]);
        return RsaKeyImportExport.ImportPublicKeyFromCertificate(cert);
    }
}
