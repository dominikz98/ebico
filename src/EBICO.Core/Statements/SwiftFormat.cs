using System.Globalization;
using System.Text;

namespace EBICO.Core.Statements;

/// <summary>
/// Shared SWIFT MT (MT940/MT942) formatting primitives: the comma-decimal amount, the SWIFT date forms,
/// the credit/debit mark and CRLF line assembly. Kept in one place so the two MT builders format
/// identically.
/// </summary>
internal static class SwiftFormat
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // SWIFT amounts use a comma as the decimal separator, no thousands separator, two fraction digits.
    internal static string Amount(decimal value)
        => value.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');

    internal static string Date6(DateOnly date) => date.ToString("yyMMdd", CultureInfo.InvariantCulture);

    internal static string EntryDate4(DateOnly date) => date.ToString("MMdd", CultureInfo.InvariantCulture);

    internal static string Mark(CreditDebitIndicator indicator)
        => indicator == CreditDebitIndicator.Credit ? "C" : "D";

    internal static byte[] ToBytes(IEnumerable<string> lines)
        => Utf8NoBom.GetBytes(string.Join("\r\n", lines) + "\r\n");
}
