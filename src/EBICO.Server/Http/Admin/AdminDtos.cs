namespace EBICO.Server.Http.Admin;

/// <summary>JSON representation of a subscriber permission (order type × signature class).</summary>
/// <param name="OrderType">The order/BTF type (e.g. <c>"CCT"</c>, <c>"STA"</c>).</param>
/// <param name="SignatureClass">The signature class name (<c>E</c>, <c>A</c>, <c>B</c> or <c>T</c>).</param>
public sealed record SubscriberPermissionDto(string OrderType, string SignatureClass);

/// <summary>JSON representation of a bank returned by the admin API.</summary>
/// <param name="HostId">The bank's host identifier.</param>
/// <param name="Name">Optional human-readable name.</param>
/// <param name="SupportedVersions">The EBICS version codes the host supports (e.g. <c>"H004"</c>).</param>
public sealed record BankDto(string HostId, string? Name, IReadOnlyList<string> SupportedVersions);

/// <summary>Request body for creating or updating a bank (the host id comes from the route).</summary>
/// <param name="Name">Optional human-readable name.</param>
/// <param name="SupportedVersions">
/// Optional EBICS version codes the host supports (e.g. <c>"H004"</c>); defaults to all when omitted.
/// </param>
public sealed record BankUpsertDto(string? Name, IReadOnlyList<string>? SupportedVersions);

/// <summary>JSON representation of a partner returned by the admin API.</summary>
/// <param name="HostId">The host identifier of the bank the partner belongs to.</param>
/// <param name="PartnerId">The partner's identifier, unique within the bank.</param>
/// <param name="Name">Optional human-readable name.</param>
public sealed record PartnerDto(string HostId, string PartnerId, string? Name);

/// <summary>Request body for creating or updating a partner (host id and partner id come from the route).</summary>
/// <param name="Name">Optional human-readable name.</param>
public sealed record PartnerUpsertDto(string? Name);

/// <summary>JSON representation of a subscriber returned by the admin API.</summary>
/// <param name="HostId">The host identifier of the bank.</param>
/// <param name="PartnerId">The partner the subscriber belongs to.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="SystemId">Optional system identifier for technical / multi-user subscribers.</param>
/// <param name="State">The lifecycle state (<c>New</c>, <c>Initialized</c>, <c>Ready</c>, <c>Suspended</c>).</param>
/// <param name="Permissions">The order authorisations held by the subscriber.</param>
public sealed record SubscriberDto(
    string HostId,
    string PartnerId,
    string UserId,
    string? SystemId,
    string State,
    IReadOnlyList<SubscriberPermissionDto> Permissions);

/// <summary>
/// Request body for creating or updating a subscriber (host id, partner id and user id come from
/// the route).
/// </summary>
/// <param name="SystemId">Optional system identifier for technical / multi-user subscribers.</param>
/// <param name="State">
/// Optional initial lifecycle state; defaults to <c>New</c> on create. Note that ongoing lifecycle
/// changes should use the dedicated state endpoint, which validates the transition.
/// </param>
/// <param name="Permissions">Optional order authorisations held by the subscriber.</param>
public sealed record SubscriberUpsertDto(
    string? SystemId,
    string? State,
    IReadOnlyList<SubscriberPermissionDto>? Permissions);

/// <summary>Request body for a subscriber lifecycle transition.</summary>
/// <param name="Target">The desired lifecycle state (<c>Initialized</c>, <c>Ready</c>, <c>Suspended</c>, …).</param>
public sealed record StateTransitionDto(string Target);

/// <summary>
/// Request body for making order data available for download (issue #33). The subscriber, order type
/// come from the route; the payload is the raw plaintext order data, base64-encoded.
/// </summary>
/// <param name="Base64Data">The base64-encoded plaintext order data to enqueue for download.</param>
public sealed record DownloadDataDto(string Base64Data);

/// <summary>Status of the download queue for a (subscriber, order type): how many payloads are pending.</summary>
/// <param name="Pending">The number of payloads currently available for download.</param>
public sealed record DownloadDataStatusDto(int Pending);
