using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using EBICO.Core.Versioning;
using EBICO.Server.Pipeline;
using EBICO.Server.State;
using Microsoft.Extensions.DependencyInjection;

namespace EBICO.Tests.Server;

/// <summary>
/// Tests for the SPR order handlers (issue #29): the suspension order moves a subscriber into
/// <see cref="SubscriberState.Suspended"/>. SPR arrives as a signed <c>ebicsRequest</c> with no order
/// data and is answered with an <c>ebicsResponse</c>. Exercised end-to-end through
/// <see cref="EbicsRequestPipeline"/>.
/// </summary>
public class SprOrderHandlerTests
{
    private const string Host = "EBICOHOST";
    private const string Partner = "PARTNER01";
    private const string User = "USER01";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public static TheoryData<EbicsVersion> AllVersions =>
        [EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005];

    // States from which suspension is allowed (New/Initialized/Ready -> Suspended).
    public static TheoryData<SubscriberState> SuspendableStates =>
        [SubscriberState.New, SubscriberState.Initialized, SubscriberState.Ready];

    // --- Happy path ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Spr_SuspendsReadySubscriber_AndAnswersWithEbicsResponse(EbicsVersion version)
    {
        var (pipeline, master) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Ready);
        var xml = ServerTestHelpers.BuildSprRequest(version, Host, Partner, User);

        var result = await pipeline.ProcessAsync(xml, _ct);

        // SPR is a signed ebicsRequest, so it is answered with an ebicsResponse (000000/000000).
        var envelope = Deserialize(result);
        envelope.GetType().Name.Should().Be("EbicsResponse");
        result.Version.Should().Be(version);
        var (headerCode, bodyCode) = ServerTestHelpers.ReadReturnCodes(envelope);
        headerCode.Should().Be("000000");
        bodyCode.Should().Be("000000");

        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Suspended);
    }

    [Theory]
    [MemberData(nameof(SuspendableStates))]
    public async Task Spr_SuspendsSubscriber_FromAnyActiveState(SubscriberState state)
    {
        var (pipeline, master) = BuildServer();
        await SeedSubscriberAsync(master, state);
        var xml = ServerTestHelpers.BuildSprRequest(EbicsVersion.H005, Host, Partner, User);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("000000");
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Suspended);
    }

    // --- Unknown / already suspended (091002) ----------------------------------------------

    [Fact]
    public async Task Spr_ForUnknownSubscriber_ReturnsInvalidUserOrUserState()
    {
        var (pipeline, master) = BuildServer();
        // Bank + partner exist, but the subscriber was never created.
        await master.SaveBankAsync(new Bank(HostId.Create(Host)), _ct);
        await master.SavePartnerAsync(new Partner(HostId.Create(Host), PartnerId.Create(Partner)), _ct);
        var xml = ServerTestHelpers.BuildSprRequest(EbicsVersion.H004, Host, Partner, User);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public async Task Spr_WhenAlreadySuspended_ReturnsInvalidUserOrUserState(EbicsVersion version)
    {
        var (pipeline, master) = BuildServer();
        await SeedSubscriberAsync(master, SubscriberState.Suspended);
        var xml = ServerTestHelpers.BuildSprRequest(version, Host, Partner, User);

        var result = await pipeline.ProcessAsync(xml, _ct);

        ServerTestHelpers.ReadReturnCodes(Deserialize(result)).BodyCode.Should().Be("091002");
        // Still suspended (unchanged).
        var subscriber = await master.GetSubscriberAsync(HostId.Create(Host), PartnerId.Create(Partner), UserId.Create(User), _ct);
        subscriber!.State.Should().Be(SubscriberState.Suspended);
    }

    // --- Helpers ---------------------------------------------------------------------------

    private static (IEbicsRequestPipeline Pipeline, IMasterDataManager Master) BuildServer()
    {
        var provider = new ServiceCollection().AddEbicoServer().BuildServiceProvider();
        return (
            provider.GetRequiredService<IEbicsRequestPipeline>(),
            provider.GetRequiredService<IMasterDataManager>());
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

        if (state == SubscriberState.Suspended)
        {
            // New -> Suspended is a permitted edge.
            await master.TransitionSubscriberAsync(host, partner, user, SubscriberState.Suspended, _ct);
        }
    }

    private static IEbicsEnvelope Deserialize(EbicsPipelineResult result)
        => EbicsXmlSerializer.DeserializeEnvelope(Encoding.UTF8.GetString(result.Body));
}
