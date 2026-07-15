namespace EBICO.Core.Domain;

/// <summary>
/// A bank account held by a partner (customer), surfaced by the customer/subscriber data download orders
/// HTD/HKD (issue #41) as the <c>AccountInfo</c> element. Named <see cref="BankAccount"/> to avoid a clash
/// with the generated schema <c>AccountType</c>. Immutable value type; all components except
/// <see cref="Currency"/> are optional.
/// </summary>
/// <param name="Iban">The account number in international (IBAN) format, or <see langword="null"/>.</param>
/// <param name="Bic">The bank code in international (SWIFT-BIC) format, or <see langword="null"/>.</param>
/// <param name="Holder">The account holder's name, or <see langword="null"/>.</param>
/// <param name="Currency">The ISO 4217 currency code; defaults to <c>"EUR"</c>.</param>
/// <param name="Description">A human-readable description of the account, or <see langword="null"/>.</param>
/// <param name="Id">
/// The account identifier (the <c>AccountInfo/@ID</c> attribute, also referenced by a subscriber
/// permission's <c>AccountID</c>), or <see langword="null"/>.
/// </param>
public sealed record BankAccount(
    string? Iban = null,
    string? Bic = null,
    string? Holder = null,
    string Currency = "EUR",
    string? Description = null,
    string? Id = null);
