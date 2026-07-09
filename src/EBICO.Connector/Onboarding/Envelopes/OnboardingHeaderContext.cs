namespace EBICO.Connector.Onboarding.Envelopes;

/// <summary>
/// The subscriber/connection identifiers a builder needs to populate an EBICS request header,
/// projected from the connection so the version-specific builders stay free of connector
/// configuration types.
/// </summary>
/// <param name="HostId">The bank <c>HostID</c>.</param>
/// <param name="PartnerId">The customer <c>PartnerID</c>.</param>
/// <param name="UserId">The subscriber <c>UserID</c>.</param>
/// <param name="SystemId">The optional technical <c>SystemID</c>.</param>
public sealed record OnboardingHeaderContext(string HostId, string PartnerId, string UserId, string? SystemId = null);
