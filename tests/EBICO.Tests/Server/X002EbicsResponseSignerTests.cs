using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Core.ReturnCodes;
using EBICO.Core.Schema.XmlDsig;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;
using EBICO.Server.Transactions;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for <see cref="X002EbicsResponseSigner"/> — the outbound half of the EBICS authentication
/// signature (issue #143). A real bank signs its <c>ebicsResponse</c> and real clients verify it; an
/// unsigned response made every order after onboarding fail on such a client. These tests assert the
/// signature is actually produced, actually verifies against the bank's key, and is withheld exactly
/// where the protocol has no place for it.
/// </summary>
public class X002EbicsResponseSignerTests
{
    private static readonly HostId Host = HostId.Create("EBICOTST");

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>Every supported protocol version — the signature path is version-agnostic and must stay so.</summary>
    public static TheoryData<EbicsVersion> Versions => [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Transaction_response_is_signed_and_verifies_against_the_bank_key(EbicsVersion version)
    {
        var fixture = await CreateAsync(seedBank: true, _ct);

        var body = await fixture.Signer.SerializeAsync(BuildTransactionResponse(version), Host, _ct);

        var signature = ReadSignature(body);
        signature.Should().NotBeNull("a bank signs its ebicsResponse");

        var keys = await fixture.BankKeys.GetOrCreateAsync(Host, _ct);
        AuthenticationSignature
            .Verify(Text(body), signature!, keys.Authentication.ToPublicOnly(), keys.AuthenticationVersion)
            .Should().BeTrue("the client verifies with the public key it received via HPB");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Signature_uses_the_declared_x002_algorithms(EbicsVersion version)
    {
        var fixture = await CreateAsync(seedBank: true, _ct);

        var body = await fixture.Signer.SerializeAsync(BuildTransactionResponse(version), Host, _ct);

        var signedInfo = ReadSignature(body)!.SignedInfo!;
        signedInfo.SignatureMethod!.Algorithm.Should().Be(AuthenticationSignature.SignatureMethodAlgorithm);
        signedInfo.Reference[0].DigestMethod!.Algorithm.Should().Be(AuthenticationSignature.DigestMethodAlgorithm);
        signedInfo.Reference[0].Uri.Should().Be(AuthenticationSignature.AuthenticatedNodesReferenceUri);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Signature_does_not_verify_against_a_different_key(EbicsVersion version)
    {
        var fixture = await CreateAsync(seedBank: true, _ct);
        var body = await fixture.Signer.SerializeAsync(BuildTransactionResponse(version), Host, _ct);
        var keys = await fixture.BankKeys.GetOrCreateAsync(Host, _ct);

        AuthenticationSignature
            .Verify(Text(body), ReadSignature(body)!, RsaKeyMaterial.Generate().ToPublicOnly(), keys.AuthenticationVersion)
            .Should().BeFalse("a signature that verifies against any key would authenticate nothing");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Tampering_with_the_authenticated_header_breaks_verification(EbicsVersion version)
    {
        var fixture = await CreateAsync(seedBank: true, _ct);
        var body = await fixture.Signer.SerializeAsync(BuildTransactionResponse(version), Host, _ct);
        var keys = await fixture.BankKeys.GetOrCreateAsync(Host, _ct);

        // Flip the return code in the (authenticated) header — what an attacker on the wire would do to
        // turn a rejection into an acceptance, or the reverse.
        var original = Text(body);
        var tampered = original.Replace(
            EbicsReturnCode.Ok.Code, EbicsReturnCode.MaxSegmentsExceeded.Code, StringComparison.Ordinal);
        tampered.Should().NotBe(original, "a no-op fixture would pass for the wrong reason");

        AuthenticationSignature
            .Verify(tampered, ReadSignature(body)!, keys.Authentication.ToPublicOnly(), keys.AuthenticationVersion)
            .Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Key_management_response_stays_unsigned(EbicsVersion version)
    {
        var fixture = await CreateAsync(seedBank: true, _ct);
        var response = new EbicsResponseFactory().BuildKeyManagementResponse(version, EbicsReturnCode.Ok);

        var body = await fixture.Signer.SerializeAsync(response, Host, _ct);

        // INI/HIA/HPB precede or bootstrap the key exchange, so their schema has no AuthSignature at all.
        response.Should().NotBeAssignableTo<IAuthSignedResponseEnvelope>();
        Text(body).Should().NotContain("AuthSignature");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Response_without_a_host_stays_unsigned(EbicsVersion version)
    {
        // Malformed XML never yields a HostID, so there is no bank identity to sign as.
        var fixture = await CreateAsync(seedBank: true, _ct);

        var body = await fixture.Signer.SerializeAsync(BuildTransactionResponse(version), host: null, _ct);

        ReadSignature(body).Should().BeNull();
        fixture.BankKeys.MintedHosts.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Unknown_host_stays_unsigned_and_mints_no_key_pair(EbicsVersion version)
    {
        var fixture = await CreateAsync(seedBank: false, _ct);

        var body = await fixture.Signer.SerializeAsync(BuildTransactionResponse(version), Host, _ct);

        ReadSignature(body).Should().BeNull();

        // Asking the key store for an unknown host would mint an RSA key pair per bogus HostID, so the
        // signer consults the master data first (as HpbOrderHandlerBase does).
        fixture.BankKeys.MintedHosts.Should().Be(0, "an unknown HostID must not make the emulator generate key pairs");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Unsigned_signer_never_signs(EbicsVersion version)
    {
        var body = await new UnsignedEbicsResponseSigner().SerializeAsync(BuildTransactionResponse(version), Host, _ct);

        ReadSignature(body).Should().BeNull();
    }

    [Fact]
    public async Task Signing_an_already_signed_envelope_re_signs_over_the_unsigned_form()
    {
        var fixture = await CreateAsync(seedBank: true, _ct);
        var response = BuildTransactionResponse(EbicsVersion.H004);

        var first = await fixture.Signer.SerializeAsync(response, Host, _ct);
        var second = await fixture.Signer.SerializeAsync(response, Host, _ct);

        // AuthSignature is not part of the authenticated node-set, so re-signing a signed envelope must
        // reproduce the same bytes rather than fold the previous signature into the digest.
        second.Should().Equal(first);
    }

    [Fact]
    public async Task Public_only_bank_key_fails_loudly_rather_than_answering_unsigned()
    {
        // IServerBankKeyStore.SetAsync accepts a public-only pair (the public part alone serves HPB), so
        // this is a reachable misconfiguration. Answering unsigned instead would silently reintroduce
        // exactly the defect the signer exists to remove.
        var fixture = await CreateAsync(seedBank: true, _ct);
        var generated = await fixture.BankKeys.GetOrCreateAsync(Host, _ct);
        await fixture.BankKeys.SetAsync(
            Host,
            generated with { Authentication = generated.Authentication.ToPublicOnly() },
            _ct);

        var act = async () => await fixture.Signer.SerializeAsync(BuildTransactionResponse(EbicsVersion.H004), Host, _ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no private authentication key");
    }

    [Fact]
    public async Task Null_response_is_rejected()
    {
        var fixture = await CreateAsync(seedBank: true, _ct);

        var signing = async () => await fixture.Signer.SerializeAsync(null!, Host, _ct);
        await signing.Should().ThrowAsync<ArgumentNullException>();

        var unsigned = async () => await new UnsignedEbicsResponseSigner().SerializeAsync(null!, Host, _ct);
        await unsigned.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_collaborators()
    {
        var store = new CountingBankKeyStore();
        var master = new MasterDataManager(new InMemoryEbicsStateStore());

        ((Action)(() => _ = new X002EbicsResponseSigner(null!, master))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new X002EbicsResponseSigner(store, null!))).Should().Throw<ArgumentNullException>();
    }

    private static async Task<Fixture> CreateAsync(bool seedBank, CancellationToken ct)
    {
        var bankKeys = new CountingBankKeyStore();
        var masterData = new MasterDataManager(new InMemoryEbicsStateStore());
        if (seedBank)
        {
            await masterData.SaveBankAsync(new Bank(Host), ct);
        }

        return new Fixture(new X002EbicsResponseSigner(bankKeys, masterData), bankKeys, masterData);
    }

    private static IEbicsResponseEnvelope BuildTransactionResponse(EbicsVersion version)
        => new EbicsResponseFactory().BuildTransactionResponse(
            version, EbicsTransactionPhase.Initialisation, [0x01, 0x02, 0x03, 0x04], EbicsReturnCode.Ok);

    private static string Text(byte[] body) => System.Text.Encoding.UTF8.GetString(body);

    private static SignatureType? ReadSignature(byte[] body)
        => EbicsXmlSerializer.DeserializeEnvelope(Text(body)) is IAuthSignedResponseEnvelope signed
            ? signed.AuthSignature
            : null;

    /// <summary>A bank key store that records how many hosts it was asked to mint a key pair for.</summary>
    private sealed class CountingBankKeyStore : IServerBankKeyStore
    {
        private readonly InMemoryServerBankKeyStore _inner = new();
        private readonly HashSet<HostId> _minted = [];

        public int MintedHosts => _minted.Count;

        public async Task<BankKeyPair> GetOrCreateAsync(HostId host, CancellationToken ct = default)
        {
            _minted.Add(host);
            return await _inner.GetOrCreateAsync(host, ct);
        }

        public Task SetAsync(HostId host, BankKeyPair keys, CancellationToken ct = default)
            => _inner.SetAsync(host, keys, ct);
    }

    private sealed record Fixture(
        X002EbicsResponseSigner Signer,
        CountingBankKeyStore BankKeys,
        IMasterDataManager MasterData);
}
