using EBICO.Core;
using EBICO.Core.Domain;
using EBICO.Server.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EBICO.Server.Http.Admin;

/// <summary>
/// Maps the emulator's master-data admin API: a small REST/JSON surface over
/// <see cref="IMasterDataManager"/> to create, read, update and delete banks, partners and
/// subscribers (Stammdatenverwaltung, issue #30).
/// </summary>
/// <remarks>
/// The API is <b>unauthenticated</b> by design — it targets local/emulator use (like Azurite).
/// Do not expose it on an untrusted network; authentication/authorisation is a later concern.
/// </remarks>
public static class EbicoAdminApiRouteBuilderExtensions
{
    /// <summary>
    /// Maps the admin API endpoints under <paramref name="prefix"/> (default <c>/admin</c>): nested
    /// resources <c>/banks</c>, <c>/banks/{hostId}/partners</c> and
    /// <c>/banks/{hostId}/partners/{partnerId}/subscribers</c>, plus subscriber permission and
    /// state sub-resources.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="prefix">The route prefix the admin API is mounted at.</param>
    /// <returns>The route group builder for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is null or empty.</exception>
    public static RouteGroupBuilder MapEbicoAdminApi(this IEndpointRouteBuilder endpoints, string prefix = "/admin")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var group = endpoints.MapGroup(prefix).WithTags("EBICO Admin");

        MapBankEndpoints(group);
        MapPartnerEndpoints(group);
        MapSubscriberEndpoints(group);

        return group;
    }

    private static void MapBankEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/banks", (IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var banks = await manager.GetBanksAsync(http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(banks.Select(ToDto).ToArray());
        }));

        group.MapGet("/banks/{hostId}", (string hostId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var bank = await manager.GetBankAsync(HostId.Create(hostId), http.RequestAborted).ConfigureAwait(false);
            return bank is null ? Results.NotFound() : Results.Ok(ToDto(bank));
        }));

        group.MapPut("/banks/{hostId}", (string hostId, BankUpsertDto? body, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var dto = body ?? new BankUpsertDto(null, null);
            var bank = new Bank(HostId.Create(hostId), dto.Name, ParseVersions(dto.SupportedVersions));
            var stored = await manager.SaveBankAsync(bank, http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(ToDto(stored));
        }));

        group.MapDelete("/banks/{hostId}", (string hostId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var removed = await manager.DeleteBankAsync(HostId.Create(hostId), http.RequestAborted).ConfigureAwait(false);
            return removed ? Results.NoContent() : Results.NotFound();
        }));
    }

    private static void MapPartnerEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/banks/{hostId}/partners", (string hostId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var partners = await manager.GetPartnersAsync(HostId.Create(hostId), http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(partners.Select(ToDto).ToArray());
        }));

        group.MapGet("/banks/{hostId}/partners/{partnerId}", (string hostId, string partnerId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var partner = await manager.GetPartnerAsync(HostId.Create(hostId), PartnerId.Create(partnerId), http.RequestAborted).ConfigureAwait(false);
            return partner is null ? Results.NotFound() : Results.Ok(ToDto(partner));
        }));

        group.MapPut("/banks/{hostId}/partners/{partnerId}", (string hostId, string partnerId, PartnerUpsertDto? body, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var partner = new Partner(HostId.Create(hostId), PartnerId.Create(partnerId), body?.Name);
            var stored = await manager.SavePartnerAsync(partner, http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(ToDto(stored));
        }));

        group.MapDelete("/banks/{hostId}/partners/{partnerId}", (string hostId, string partnerId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var removed = await manager.DeletePartnerAsync(HostId.Create(hostId), PartnerId.Create(partnerId), http.RequestAborted).ConfigureAwait(false);
            return removed ? Results.NoContent() : Results.NotFound();
        }));
    }

    private static void MapSubscriberEndpoints(RouteGroupBuilder group)
    {
        const string collection = "/banks/{hostId}/partners/{partnerId}/subscribers";
        const string item = collection + "/{userId}";

        group.MapGet(collection, (string hostId, string partnerId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var subscribers = await manager.GetSubscribersAsync(HostId.Create(hostId), PartnerId.Create(partnerId), http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(subscribers.Select(ToDto).ToArray());
        }));

        group.MapGet(item, (string hostId, string partnerId, string userId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var subscriber = await manager.GetSubscriberAsync(HostId.Create(hostId), PartnerId.Create(partnerId), UserId.Create(userId), http.RequestAborted).ConfigureAwait(false);
            return subscriber is null ? Results.NotFound() : Results.Ok(ToDto(subscriber));
        }));

        group.MapPut(item, (string hostId, string partnerId, string userId, SubscriberUpsertDto? body, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var dto = body ?? new SubscriberUpsertDto(null, null, null);
            var subscriber = new Subscriber(
                HostId.Create(hostId),
                PartnerId.Create(partnerId),
                UserId.Create(userId),
                ParseSystemId(dto.SystemId),
                ParseState(dto.State),
                MapPermissions(dto.Permissions));
            var stored = await manager.SaveSubscriberAsync(subscriber, http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(ToDto(stored));
        }));

        group.MapDelete(item, (string hostId, string partnerId, string userId, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var removed = await manager.DeleteSubscriberAsync(HostId.Create(hostId), PartnerId.Create(partnerId), UserId.Create(userId), http.RequestAborted).ConfigureAwait(false);
            return removed ? Results.NoContent() : Results.NotFound();
        }));

        group.MapPut(item + "/permissions", (string hostId, string partnerId, string userId, SubscriberPermissionDto[]? body, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            var permissions = MapPermissions(body);
            var updated = await manager.SetPermissionsAsync(HostId.Create(hostId), PartnerId.Create(partnerId), UserId.Create(userId), permissions, http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(ToDto(updated));
        }));

        group.MapPost(item + "/state", (string hostId, string partnerId, string userId, StateTransitionDto? body, IMasterDataManager manager, HttpContext http) => Guard(async () =>
        {
            if (body is null)
            {
                return Results.Problem("A target state is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var updated = await manager.TransitionSubscriberAsync(
                HostId.Create(hostId), PartnerId.Create(partnerId), UserId.Create(userId),
                ParseState(body.Target), http.RequestAborted).ConfigureAwait(false);
            return Results.Ok(ToDto(updated));
        }));
    }

    // --- Exception mapping -----------------------------------------------------------------

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (UnknownSubscriberException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (MasterDataException ex)
        {
            // Referential-integrity violations (unknown bank/partner) — the request conflicts with state.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidSubscriberStateTransitionException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (EbicsDomainException ex)
        {
            // Invalid identifiers (e.g. bad HostID/PartnerID/UserID in the route).
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (FormatException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    // --- Domain <-> DTO mapping ------------------------------------------------------------

    private static BankDto ToDto(Bank bank)
        => new(bank.HostId.Value, bank.Name, bank.SupportedVersions.Select(v => v.ToString()).ToArray());

    private static PartnerDto ToDto(Partner partner)
        => new(partner.HostId.Value, partner.PartnerId.Value, partner.Name);

    private static SubscriberDto ToDto(Subscriber subscriber)
        => new(
            subscriber.HostId.Value,
            subscriber.PartnerId.Value,
            subscriber.UserId.Value,
            subscriber.SystemId?.Value,
            subscriber.State.ToString(),
            subscriber.Permissions.Select(p => new SubscriberPermissionDto(p.OrderType, p.SignatureClass.ToString())).ToArray());

    private static IReadOnlyList<EbicsVersion>? ParseVersions(IReadOnlyList<string>? versions)
    {
        if (versions is null)
        {
            return null;
        }

        return versions.Select(ParseVersion).ToArray();
    }

    private static EbicsVersion ParseVersion(string value)
        => Enum.TryParse<EbicsVersion>(value, ignoreCase: true, out var version) && Enum.IsDefined(version)
            ? version
            : throw new FormatException($"'{value}' is not a valid EBICS version (expected H003, H004 or H005).");

    private static SubscriberState ParseState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SubscriberState.New;
        }

        return Enum.TryParse<SubscriberState>(value, ignoreCase: true, out var state) && Enum.IsDefined(state)
            ? state
            : throw new FormatException($"'{value}' is not a valid subscriber state.");
    }

    private static SignatureClass ParseSignatureClass(string value)
        => Enum.TryParse<SignatureClass>(value, ignoreCase: true, out var signatureClass) && Enum.IsDefined(signatureClass)
            ? signatureClass
            : throw new FormatException($"'{value}' is not a valid signature class (expected E, A, B or T).");

    private static SystemId? ParseSystemId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : SystemId.Create(value);

    private static IReadOnlyList<SubscriberPermission> MapPermissions(IReadOnlyList<SubscriberPermissionDto>? permissions)
    {
        if (permissions is null)
        {
            return [];
        }

        return permissions
            .Select(p => new SubscriberPermission(p.OrderType, ParseSignatureClass(p.SignatureClass)))
            .ToArray();
    }
}
