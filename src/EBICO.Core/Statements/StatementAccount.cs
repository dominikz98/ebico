namespace EBICO.Core.Statements;

/// <summary>
/// The account a statement is drawn for. A value object holding the identifiers a statement renders (IBAN,
/// BIC, owner) and the single currency all its amounts and balances are expressed in.
/// </summary>
/// <param name="Iban">The account IBAN (e.g. a German <c>DE</c> IBAN with valid check digits).</param>
/// <param name="Bic">The account servicer BIC (e.g. <c>EBICODEMMXXX</c>).</param>
/// <param name="Currency">The ISO 4217 currency code all amounts/balances use (e.g. <c>EUR</c>).</param>
/// <param name="OwnerName">The account owner's display name.</param>
/// <param name="AccountNumber">The domestic account number (the IBAN's account-number part).</param>
public readonly record struct StatementAccount(
    string Iban,
    string Bic,
    string Currency,
    string OwnerName,
    string AccountNumber);
