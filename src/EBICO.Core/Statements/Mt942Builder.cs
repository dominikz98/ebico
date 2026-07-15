using System.Globalization;

namespace EBICO.Core.Statements;

/// <summary>
/// Builds a minimal, structurally plausible SWIFT <b>MT942</b> Interim Transaction Report (order type
/// <see cref="OrderType"/>, issue #40) from an <see cref="AccountStatement"/>. Unlike MT940 an interim
/// report carries <b>no</b> booked opening/closing balance (<c>:60F:</c>/<c>:62F:</c>); instead it emits a
/// floor limit (<c>:34F:</c>), a date/time indication (<c>:13D:</c>), one <c>:61:</c>/<c>:86:</c> pair per
/// movement and the debit/credit summaries (<c>:90D:</c>/<c>:90C:</c>).
/// </summary>
/// <remarks>
/// <b>⚠️ Spec-Vorbehalt:</b> the tag grammar is a pragmatic, minimal rendering not validated against the
/// official SWIFT MT942 field specification; the exact strings are pinned by tests.
/// </remarks>
public static class Mt942Builder
{
    /// <summary>The classical download order type this builder produces (<c>VMK</c>).</summary>
    public const string OrderType = StatementOrderTypes.InterimReportMt942;

    /// <summary>Builds the MT942 interim report as deterministic CRLF text (UTF-8, no BOM).</summary>
    /// <param name="statement">The statement to render.</param>
    /// <returns>The MT942 document as UTF-8 bytes.</returns>
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
            $":34F:{ccy}{SwiftFormat.Amount(0m)}",
            $":13D:{statement.CreationTimestamp.ToString("yyMMddHHmm", CultureInfo.InvariantCulture)}{Offset(statement.CreationTimestamp)}",
        };

        var debitCount = 0;
        var creditCount = 0;
        var debitSum = 0m;
        var creditSum = 0m;

        foreach (var entry in statement.Entries)
        {
            lines.Add(
                $":61:{SwiftFormat.Date6(entry.ValueDate)}{SwiftFormat.EntryDate4(entry.BookingDate)}"
                + $"{SwiftFormat.Mark(entry.CreditDebit)}{SwiftFormat.Amount(entry.Amount)}NTRF{entry.Reference}");
            lines.Add($":86:{entry.RemittanceInfo} {entry.CounterpartyName}");

            if (entry.CreditDebit == CreditDebitIndicator.Credit)
            {
                creditCount++;
                creditSum += entry.Amount;
            }
            else
            {
                debitCount++;
                debitSum += entry.Amount;
            }
        }

        lines.Add($":90D:{debitCount.ToString(CultureInfo.InvariantCulture)}{ccy}{SwiftFormat.Amount(debitSum)}");
        lines.Add($":90C:{creditCount.ToString(CultureInfo.InvariantCulture)}{ccy}{SwiftFormat.Amount(creditSum)}");
        return SwiftFormat.ToBytes(lines);
    }

    // :13D: time zone indication = {sign}{HHMM}, e.g. +0100.
    private static string Offset(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"{sign}{Math.Abs(offset.Hours):D2}{Math.Abs(offset.Minutes):D2}";
    }
}
