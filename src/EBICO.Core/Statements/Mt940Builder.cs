using System.Globalization;

namespace EBICO.Core.Statements;

/// <summary>
/// Builds a minimal, structurally plausible SWIFT <b>MT940</b> Customer Statement (order type
/// <see cref="OrderType"/>, issue #40) from an <see cref="AccountStatement"/>. The output is deterministic
/// CRLF-delimited text (UTF-8, no BOM) carrying the tags <c>:20: :25: :28C: :60F:</c>, one
/// <c>:61:</c>/<c>:86:</c> pair per booking and the closing <c>:62F:</c> balance.
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the tag grammar (especially the <c>:61:</c> field and the free-text
/// <c>:86:</c> narrative) is a pragmatic, minimal rendering and is not validated against the official
/// SWIFT MT940 field specification. There is no XSD for MT messages; the exact strings are pinned by tests.
/// </remarks>
public static class Mt940Builder
{
    /// <summary>The classical download order type this builder produces (<c>STA</c>).</summary>
    public const string OrderType = StatementOrderTypes.StatementMt940;

    /// <summary>Builds the MT940 statement as deterministic CRLF text (UTF-8, no BOM).</summary>
    /// <param name="statement">The statement to render.</param>
    /// <returns>The MT940 document as UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="statement"/> is <see langword="null"/>.</exception>
    public static byte[] Build(AccountStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var ccy = statement.Account.Currency;
        var lines = new List<string>
        {
            $":20:{statement.StatementId}",
            $":25:{statement.Account.Iban}",
            $":28C:{statement.StatementNumber.ToString(CultureInfo.InvariantCulture)}/{statement.SequenceNumber.ToString(CultureInfo.InvariantCulture)}",
            Balance("60F", statement.OpeningBalance, ccy),
        };

        foreach (var entry in statement.Entries)
        {
            lines.Add(
                $":61:{SwiftFormat.Date6(entry.ValueDate)}{SwiftFormat.EntryDate4(entry.BookingDate)}"
                + $"{SwiftFormat.Mark(entry.CreditDebit)}{SwiftFormat.Amount(entry.Amount)}NTRF{entry.Reference}");
            lines.Add($":86:{entry.RemittanceInfo} {entry.CounterpartyName}");
        }

        lines.Add(Balance("62F", statement.ClosingBalance, ccy));
        return SwiftFormat.ToBytes(lines);
    }

    // :60F:/:62F: booked balance = {C|D}{YYMMDD}{CCY}{amount}, e.g. :60F:C260701EUR1234,56
    private static string Balance(string tag, StatementBalance balance, string currency)
        => $":{tag}:{SwiftFormat.Mark(balance.CreditDebit)}{SwiftFormat.Date6(balance.Date)}{currency}{SwiftFormat.Amount(balance.Amount)}";
}
