using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Server.Pipeline;
using EBICO.Server.ReturnCodes;
using EBICO.Server.State;

namespace EBICO.Server.Handlers;

/// <summary>
/// Base for the version-specific SPR order handlers. SPR (<c>OrderType == "SPR"</c>, "Sperrung") is the
/// suspension order: it moves the subscriber into <see cref="SubscriberState.Suspended"/> so it can no
/// longer transact until it is reactivated (<see cref="SubscriberState.Suspended"/> →
/// <see cref="SubscriberState.Ready"/>, via the admin API / <see cref="IMasterDataManager"/>).
/// </summary>
/// <remarks>
/// <para>
/// SPR arrives as a signed <c>ebicsRequest</c> upload but carries <b>no</b> order-data key material
/// (there is no <c>SPRRequestOrderDataType</c> in the schema), so — unlike HCA/HCS — nothing is
/// decrypted: the handler only reads the subscriber identifiers from the request header and performs
/// the lifecycle transition. Any <c>DataTransfer</c>/order signature present is ignored.
/// </para>
/// <para>
/// The transition <c>New/Initialized/Ready → Suspended</c> is permitted by the domain state machine
/// (<see cref="Subscriber"/>); a subscriber that is <b>already</b> <see cref="SubscriberState.Suspended"/>
/// (or unknown) is rejected with <see cref="EbicsReturnCode.InvalidUserOrUserState"/> (a
/// <c>Suspended → Suspended</c> edge does not exist). Suspension is temporary and reversible and does
/// <b>not</b> remove the stored keys, so a later reactivation keeps them.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the request's order/authentication signature is <b>not</b> verified
/// (consistent with INI/HIA/HPB — signatures are M4); SPR maps to the existing
/// <see cref="SubscriberState.Suspended"/> rather than a dedicated permanent-block state. See
/// <c>docs/server/hca-hcs-spr-hsa.md</c>.
/// </para>
/// </remarks>
public abstract class SprOrderHandlerBase : IEbicsOrderHandler
{
    /// <summary>The order type served by SPR handlers.</summary>
    public const string SprOrderType = "SPR";

    private readonly IMasterDataManager _masterData;

    /// <summary>Initializes the handler.</summary>
    /// <param name="masterData">The master-data manager used to resolve and transition the subscriber.</param>
    /// <exception cref="ArgumentNullException"><paramref name="masterData"/> is <see langword="null"/>.</exception>
    protected SprOrderHandlerBase(IMasterDataManager masterData)
    {
        ArgumentNullException.ThrowIfNull(masterData);
        _masterData = masterData;
    }

    /// <inheritdoc />
    public abstract EbicsVersion Version { get; }

    /// <inheritdoc />
    public string OrderType => SprOrderType;

    /// <inheritdoc />
    public async Task<EbicsOrderResult> HandleAsync(EbicsRequestContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        SprRequestData request;
        try
        {
            request = ExtractHeader(context);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            // The envelope was not the expected signed ebicsRequest (e.g. an unsecured request that
            // happens to carry OrderType "SPR").
            return new EbicsOrderResult(EbicsReturnCode.InvalidOrderDataFormat);
        }

        if (!HostId.TryCreate(request.HostId, out var hostId)
            || !PartnerId.TryCreate(request.PartnerId, out var partnerId)
            || !UserId.TryCreate(request.UserId, out var userId))
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        // Suspension applies to an existing subscriber that is not already suspended. Re-suspending an
        // already-Suspended subscriber (no Suspended -> Suspended edge) and an unknown subscriber are
        // both rejected.
        var subscriber = await _masterData.GetSubscriberAsync(hostId, partnerId, userId, ct).ConfigureAwait(false);
        if (subscriber is null || subscriber.State == SubscriberState.Suspended)
        {
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        try
        {
            _ = await _masterData.TransitionSubscriberAsync(hostId, partnerId, userId, SubscriberState.Suspended, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidSubscriberStateTransitionException or UnknownSubscriberException)
        {
            // Defensive: the pre-checks above make this unreachable, but keep the mapping consistent if
            // the state changed concurrently between the read and the transition.
            return new EbicsOrderResult(EbicsReturnCode.InvalidUserOrUserState);
        }

        return new EbicsOrderResult(EbicsReturnCode.Ok);
    }

    /// <summary>
    /// Reads the subscriber identifiers out of the version-specific <c>ebicsRequest</c> in
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The request context (its <see cref="EbicsRequestContext.Envelope"/> is the signed request).</param>
    /// <returns>The extracted identifiers.</returns>
    /// <exception cref="InvalidDataException">The envelope is not the expected signed request.</exception>
    protected abstract SprRequestData ExtractHeader(EbicsRequestContext context);

    /// <summary>
    /// The subscriber identifiers extracted from an SPR request. Identifiers are the raw header strings
    /// (validated later by the base handler).
    /// </summary>
    /// <param name="HostId">The raw <c>HostID</c> from the request header.</param>
    /// <param name="PartnerId">The raw <c>PartnerID</c> from the request header.</param>
    /// <param name="UserId">The raw <c>UserID</c> from the request header.</param>
    protected sealed record SprRequestData(string? HostId, string? PartnerId, string? UserId);
}
