namespace EBICO.Core.Domain;

/// <summary>
/// A partner's (customer's) postal address, surfaced by the customer/subscriber data download orders
/// HTD/HKD (issue #41) as the <c>AddressInfo</c> element. All components are optional so a partner can
/// carry as much or as little address detail as the operator seeds. Immutable value type.
/// </summary>
/// <param name="Name">The customer's name, or <see langword="null"/>.</param>
/// <param name="Street">Street and house number, or <see langword="null"/>.</param>
/// <param name="PostCode">Postal code, or <see langword="null"/>.</param>
/// <param name="City">City, or <see langword="null"/>.</param>
/// <param name="Region">Region / province / federal state, or <see langword="null"/>.</param>
/// <param name="Country">Country, or <see langword="null"/>.</param>
public sealed record Address(
    string? Name = null,
    string? Street = null,
    string? PostCode = null,
    string? City = null,
    string? Region = null,
    string? Country = null);
