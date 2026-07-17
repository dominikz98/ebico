extern alias EbicoServer;
using AwesomeAssertions;
using EBICO.Connector;
using EBICO.Connector.Keys;
using EBICO.Connector.Onboarding;
using EBICO.Connector.Onboarding.Keys;
using EBICO.Core;
using EBICO.Core.Crypto;
using EBICO.Core.Domain;
using EBICO.Server.State;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.E2E;

using ServerProgram = EbicoServer::Program;

/// <summary>
/// Shared RSA key material for the connector↔server end-to-end suite (issue #57). RSA generation
/// dominates the cost of a round-trip and <see cref="RsaKeyMaterial.MinKeySizeBits"/> (2048) is an
/// enforced floor — the constructor rejects anything smaller — so the pool generates one key per
/// purpose once per test run instead of shrinking them. The material is immutable and every harness
/// registers it under its own HostID, so sharing cannot leak state between tests.
/// </summary>
/// <remarks>
/// The onboarding tests deliberately bypass the pool and drive the real
/// <see cref="EBICO.Connector.Onboarding.Keys.ISubscriberKeyGenerator"/>: there, key generation is part
/// of the subject under test. Everywhere else onboarding is only a precondition, so pre-seeded keys buy
/// speed without costing protocol fidelity — INI/HIA/HPB still run for real over HTTP.
/// </remarks>
internal static class E2EKeyPool
{
    private static readonly Lazy<RsaKeyMaterial> SubscriberSignatureKey = Lazily();
    private static readonly Lazy<RsaKeyMaterial> SubscriberAuthenticationKey = Lazily();
    private static readonly Lazy<RsaKeyMaterial> SubscriberEncryptionKey = Lazily();
    private static readonly Lazy<RsaKeyMaterial> BankAuthenticationKey = Lazily();
    private static readonly Lazy<RsaKeyMaterial> BankEncryptionKey = Lazily();

    /// <summary>
    /// The subscriber key pair for <paramref name="purpose"/>. Each purpose gets distinct material, so a
    /// purpose mix-up anywhere in the pipeline cannot pass silently.
    /// </summary>
    /// <param name="purpose">The key purpose.</param>
    /// <returns>The key pair, including its private part.</returns>
    public static RsaKeyMaterial Subscriber(KeyPurpose purpose) => purpose switch
    {
        KeyPurpose.Signature => SubscriberSignatureKey.Value,
        KeyPurpose.Authentication => SubscriberAuthenticationKey.Value,
        _ => SubscriberEncryptionKey.Value,
    };

    /// <summary>
    /// The bank key pair seeded into <see cref="IServerBankKeyStore"/>, mirroring the versions
    /// <c>InMemoryServerBankKeyStore</c> would have generated (X002/E002, permitted for every supported
    /// protocol version).
    /// </summary>
    /// <returns>The bank's authentication and encryption key pair.</returns>
    public static BankKeyPair BankKeys() => new(
        BankAuthenticationKey.Value,
        KeyVersions.Default(KeyPurpose.Authentication, EbicsVersion.H005).Version,
        BankEncryptionKey.Value,
        KeyVersions.Default(KeyPurpose.Encryption, EbicsVersion.H005).Version);

    private static Lazy<RsaKeyMaterial> Lazily()
        => new(static () => RsaKeyMaterial.Generate(), LazyThreadSafetyMode.ExecutionAndPublication);
}

/// <summary>
/// The connector↔server end-to-end harness (issue #57): wires a real <see cref="IEbicsClient"/> onto an
/// in-process <see cref="WebApplicationFactory{TEntryPoint}"/> host and seeds the master data the flows
/// require. Unlike the Tier-A harnesses (<c>FakeUploadServer</c> and friends) neither side is a stand-in:
/// the real connector pipeline speaks the real EBICS wire format to the real server pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Isolation is per <b>HostID</b>, not per host: every server store keys on <see cref="HostId"/> or
/// <see cref="SubscriberKeyRef"/>, so a distinct HostID per test isolates state as effectively as a fresh
/// host (the <c>WithWebHostBuilder(_ =&gt; { })</c> idiom of the server tests) without a second host boot.
/// </para>
/// <para>
/// The EBICS version is fixed on the connection at configuration time rather than per request, so each
/// harness owns one <see cref="ServiceProvider"/> speaking exactly one version.
/// </para>
/// </remarks>
internal sealed class EbicsE2EHarness : IAsyncDisposable
{
    /// <summary>
    /// The permissions a harness seeds unless overridden. The codes are the <b>classical effective</b>
    /// ones the server authorises against after <c>BtfOrderTypeCatalog.Resolve*OrderType</c>, so this one
    /// set covers H003/H004 (direct code) and H005 (BTU/BTD + BTF) alike.
    /// </summary>
    public static readonly SubscriberPermission[] DefaultPermissions =
        [new("CCT", SignatureClass.T), new("C53", SignatureClass.T)];

    private readonly ServiceProvider _provider;

    private EbicsE2EHarness(
        ServiceProvider provider,
        IServiceProvider serverServices,
        EbicsVersion version,
        HostId hostId,
        PartnerId partnerId,
        UserId userId)
    {
        _provider = provider;
        ServerServices = serverServices;
        Version = version;
        HostId = hostId;
        PartnerId = partnerId;
        UserId = userId;
    }

    /// <summary>The EBICS version this harness speaks.</summary>
    public EbicsVersion Version { get; }

    /// <summary>The bank host this harness is registered under.</summary>
    public HostId HostId { get; }

    /// <summary>The partner this harness is registered under.</summary>
    public PartnerId PartnerId { get; }

    /// <summary>The subscriber this harness is registered under.</summary>
    public UserId UserId { get; }

    /// <summary>The server's service provider, for asserting server-side state after a round-trip.</summary>
    public IServiceProvider ServerServices { get; }

    /// <summary>The connector client under test.</summary>
    public IEbicsClient Client => _provider.GetRequiredService<IEbicsClient>();

    /// <summary>The connector-side key store; the bank's public keys land here after HPB.</summary>
    public IKeyStore ConnectorKeys => _provider.GetRequiredService<IKeyStore>();

    /// <summary>
    /// The connector's subscriber key generator, for flows constructed with
    /// <c>provisionKeys: false</c> that generate their keys themselves.
    /// </summary>
    public ISubscriberKeyGenerator KeyGenerator => _provider.GetRequiredService<ISubscriberKeyGenerator>();

    /// <summary>This harness's server-side subscriber key reference.</summary>
    public SubscriberKeyRef KeyRef => new(HostId, PartnerId, UserId);

    /// <summary>
    /// Builds a harness for <paramref name="version"/> against <paramref name="factory"/>: seeds bank,
    /// partner and a <see cref="SubscriberState.New"/> subscriber, seeds the bank key pair, and wires a
    /// connector service provider whose transport points at the in-process test host.
    /// </summary>
    /// <param name="factory">The class fixture's application factory; its host is started here.</param>
    /// <param name="version">The EBICS version this harness speaks.</param>
    /// <param name="scenario">
    /// Distinguishes this harness's HostID from every other test's, which is what isolates the server
    /// state. Must be EBICS-identifier-safe (<c>[a-zA-Z0-9,=]</c>, no hyphens or underscores).
    /// </param>
    /// <param name="permissions">The subscriber's order authorisations; defaults to <see cref="DefaultPermissions"/>.</param>
    /// <param name="provisionKeys">
    /// Whether to pre-seed the subscriber keys from <see cref="E2EKeyPool"/>. Pass <see langword="false"/>
    /// when the test drives <see cref="EBICO.Connector.Onboarding.Keys.ISubscriberKeyGenerator"/> itself.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The initialized harness.</returns>
    public static async Task<EbicsE2EHarness> CreateAsync(
        WebApplicationFactory<ServerProgram> factory,
        EbicsVersion version,
        string scenario,
        IEnumerable<SubscriberPermission>? permissions = null,
        bool provisionKeys = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var hostId = HostId.Create($"E2E{version}{scenario}");
        var partnerId = PartnerId.Create($"P{version}{scenario}");
        var userId = UserId.Create($"U{version}{scenario}");

        // Touching Services starts the host, so factory.Server is live before the handler factory below
        // runs, and the seed below lands in the same container the requests will hit.
        var serverServices = factory.Services;

        var master = serverServices.GetRequiredService<IMasterDataManager>();
        await master.SaveBankAsync(new Bank(hostId), ct);
        await master.SavePartnerAsync(new Partner(hostId, partnerId), ct);

        // Left in SubscriberState.New on purpose: the real INI drives New -> Initialized and the real HIA
        // drives Initialized -> Ready. Pre-transitioning (as the single-layer server tests do) would skip
        // exactly the lifecycle this suite exists to prove.
        await master.SaveSubscriberAsync(
            new Subscriber(hostId, partnerId, userId, permissions: permissions ?? DefaultPermissions), ct);

        // Seeding beats letting InMemoryServerBankKeyStore generate lazily per HostID: it saves two RSA
        // key generations per test and lets HPB assert the exact keys back.
        await serverServices.GetRequiredService<IServerBankKeyStore>().SetAsync(hostId, E2EKeyPool.BankKeys(), ct);

        var services = new ServiceCollection();
        services.AddEbicoConnector(o =>
            {
                // HttpClientTransport posts to the absolute connection URL rather than the client's
                // BaseAddress, so this must be the test-server origin plus EbicoServerOptions.EndpointPath.
                o.Url = "http://localhost/ebics";
                o.HostId = hostId.Value;
                o.PartnerId = partnerId.Value;
                o.UserId = userId.Value;
                o.Version = version;
            })
            .ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler());
        services.AddEbicoOnboarding();
        services.AddEbicoUpload();
        services.AddEbicoDownload();
        var provider = services.BuildServiceProvider();

        if (provisionKeys)
        {
            var keys = provider.GetRequiredService<IKeyStore>();
            foreach (var purpose in (KeyPurpose[])[KeyPurpose.Signature, KeyPurpose.Authentication, KeyPurpose.Encryption])
            {
                await keys.StoreAsync(KeyOwner.Subscriber, purpose, E2EKeyPool.Subscriber(purpose), ct);
            }
        }

        return new EbicsE2EHarness(provider, serverServices, version, hostId, partnerId, userId);
    }

    /// <summary>Reads the current server-side subscriber aggregate.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The subscriber, or <see langword="null"/> when unknown to the server.</returns>
    public Task<Subscriber?> GetSubscriberAsync(CancellationToken ct)
        => ServerServices.GetRequiredService<IMasterDataManager>().GetSubscriberAsync(HostId, PartnerId, UserId, ct);

    /// <summary>
    /// Runs INI, HIA and HPB in order and returns their raw results without asserting, so the onboarding
    /// tests can assert each step themselves. Callers that need onboarding only as a precondition should
    /// chain <see cref="E2EOnboardingResults.ThrowIfFailed"/>.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The three onboarding results.</returns>
    /// <remarks>
    /// The initialization letters are suppressed: rendering them would pull the PDF renderer into every
    /// flow for no assertion value (it has its own tests). The expected bank fingerprints are passed
    /// because the bank pair is seeded from <see cref="E2EKeyPool"/> and therefore known up front — which
    /// makes the connector verify them in-flow.
    /// </remarks>
    public async Task<E2EOnboardingResults> OnboardAsync(CancellationToken ct)
    {
        var bankKeys = E2EKeyPool.BankKeys();
        var ini = await Client.Send(new IniRequest { IncludeLetter = false }, ct);
        var hia = await Client.Send(new HiaRequest { IncludeLetter = false }, ct);
        var hpb = await Client.Send(
            new HpbRequest
            {
                ExpectedAuthenticationKeyDigest = PublicKeyFingerprint.Compute(bankKeys.Authentication),
                ExpectedEncryptionKeyDigest = PublicKeyFingerprint.Compute(bankKeys.Encryption),
            },
            ct);
        return new E2EOnboardingResults(ini, hia, hpb);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

/// <summary>The three onboarding results of <see cref="EbicsE2EHarness.OnboardAsync"/>.</summary>
/// <param name="Ini">The INI result.</param>
/// <param name="Hia">The HIA result.</param>
/// <param name="Hpb">The HPB result.</param>
internal sealed record E2EOnboardingResults(
    EbicsResult<IniResult> Ini,
    EbicsResult<HiaResult> Hia,
    EbicsResult<HpbResult> Hpb)
{
    /// <summary>
    /// Fails the test when a precondition flow did not succeed, so a broken onboarding surfaces here
    /// rather than as a puzzling return code on the order actually under test.
    /// </summary>
    /// <returns>The same instance, for chaining.</returns>
    public E2EOnboardingResults ThrowIfFailed()
    {
        Ini.IsSuccess.Should().BeTrue($"INI must succeed (got {Ini.ReturnCode} {Ini.ReturnText})");
        Hia.IsSuccess.Should().BeTrue($"HIA must succeed (got {Hia.ReturnCode} {Hia.ReturnText})");
        Hpb.IsSuccess.Should().BeTrue($"HPB must succeed (got {Hpb.ReturnCode} {Hpb.ReturnText})");
        return this;
    }
}
