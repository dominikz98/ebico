using System.Globalization;
using System.Text;

namespace EBICO.Core.Statements;

/// <summary>
/// Produces a deterministic, synthetic <see cref="AccountStatement"/> for a subscriber and a reporting
/// period — the "generierbare Testdaten serverseitig" of issue #40. The output is a pure function of the
/// inputs: the same subscriber triple + period + creation timestamp always yields byte-identical statements
/// (and therefore byte-identical MT940/MT942/camt output), which is what makes the emulator's downloads
/// testable without pre-seeded fixtures.
/// </summary>
/// <remarks>
/// <para>
/// Determinism is load-bearing, so the generator deliberately avoids the two common non-deterministic
/// seams: it never reads the wall clock (the creation time is an input) and it never uses
/// <see cref="string.GetHashCode()"/> (which is randomised per process). The random seed is a stable FNV-1a
/// hash of the subscriber triple, and every draw comes from a single seeded <see cref="Random"/> in a fixed
/// order (account fields first, then day-by-day entries), so the sequence is reproducible.
/// </para>
/// <para>
/// The account carries a valid German IBAN (correct ISO 13616 / ISO 7064 MOD 97-10 check digits) so tests
/// can verify it independently. Balances follow the invariant
/// <c>Closing = Opening + Σ(credits) − Σ(debits)</c>.
/// </para>
/// </remarks>
public static class SyntheticStatementGenerator
{
    private static readonly string[] OwnerNames =
        ["Muster Handels GmbH", "Beispiel AG", "Nordwind Logistik KG", "Sonnenschein e.K.", "Alpen Bau GmbH"];

    private static readonly string[] Counterparties =
        ["Lieferant Nord GmbH", "Stadtwerke Musterstadt", "Kunde Sued AG", "Personal Lohnlauf", "Finanzamt Musterstadt", "Telekom Service GmbH"];

    private static readonly string[] Purposes =
        ["Rechnung", "Gutschrift", "Lohn/Gehalt", "Dauerauftrag", "Erstattung", "Abschlag"];

    private const string DemoBic = "EBICDEMMXXX";
    private const string Currency = "EUR";

    /// <summary>
    /// Generates a deterministic statement for the subscriber over <c>[<paramref name="rangeStart"/>,
    /// <paramref name="rangeEnd"/>]</c> (both inclusive).
    /// </summary>
    /// <param name="hostId">The bank host id of the requesting subscriber.</param>
    /// <param name="partnerId">The partner (customer) id of the requesting subscriber.</param>
    /// <param name="userId">The user id of the requesting subscriber.</param>
    /// <param name="rangeStart">The inclusive first day of the reporting period.</param>
    /// <param name="rangeEnd">The inclusive last day of the reporting period.</param>
    /// <param name="creationTimestamp">The statement creation time (rendered as camt <c>CreDtTm</c> / the ZIP entry time).</param>
    /// <returns>A fully populated, deterministic <see cref="AccountStatement"/>.</returns>
    /// <exception cref="ArgumentException">An id is null/empty/whitespace, or <paramref name="rangeEnd"/> precedes <paramref name="rangeStart"/>.</exception>
    public static AccountStatement Generate(
        string hostId,
        string partnerId,
        string userId,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateTimeOffset creationTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (rangeEnd < rangeStart)
        {
            throw new ArgumentException(
                $"The range end ({rangeEnd:O}) must not precede the range start ({rangeStart:O}).", nameof(rangeEnd));
        }

        var rng = new Random(StableSeed(hostId, partnerId, userId));

        // --- Account (drawn first, so the sequence stays stable regardless of the period) ---
        var blz = rng.Next(10_000_000, 100_000_000).ToString(CultureInfo.InvariantCulture);
        var accountNumber = rng.NextInt64(0L, 10_000_000_000L).ToString("D10", CultureInfo.InvariantCulture);
        var iban = BuildGermanIban(blz + accountNumber);
        var ownerName = $"{OwnerNames[rng.Next(OwnerNames.Length)]} ({partnerId})";
        var account = new StatementAccount(iban, DemoBic, Currency, ownerName, accountNumber);

        // Opening balance: a positive (credit-side) carry-forward between 1000.00 and 49999.99.
        var openingCents = rng.Next(100_000, 5_000_000);
        var openingBalance = new StatementBalance(StatementBalanceType.Opening, openingCents / 100m, CreditDebitIndicator.Credit, rangeStart);

        // --- Entries, day by day; maintain the running balance in signed cents (credit positive) ---
        var entries = new List<StatementEntry>();
        var runningCents = (long)openingCents;
        var sequence = 0;

        for (var day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
        {
            var perDay = rng.Next(0, 4); // 0..3 bookings per day
            for (var i = 0; i < perDay; i++)
            {
                sequence++;
                var amountCents = rng.Next(100, 1_000_000); // 1.00 .. 9999.99
                var isCredit = rng.Next(0, 2) == 0;
                var counterparty = Counterparties[rng.Next(Counterparties.Length)];
                var purpose = Purposes[rng.Next(Purposes.Length)];

                entries.Add(new StatementEntry(
                    BookingDate: day,
                    ValueDate: day,
                    Amount: amountCents / 100m,
                    CreditDebit: isCredit ? CreditDebitIndicator.Credit : CreditDebitIndicator.Debit,
                    RemittanceInfo: $"{purpose} {sequence:D6}",
                    CounterpartyName: counterparty,
                    CounterpartyIban: BuildGermanIban(
                        rng.Next(10_000_000, 100_000_000).ToString(CultureInfo.InvariantCulture)
                        + rng.NextInt64(0L, 10_000_000_000L).ToString("D10", CultureInfo.InvariantCulture)),
                    EndToEndId: $"E2E-{sequence:D6}",
                    Reference: $"REF{sequence:D6}"));

                runningCents += isCredit ? amountCents : -amountCents;
            }
        }

        var closingBalance = new StatementBalance(
            StatementBalanceType.Closing,
            Math.Abs(runningCents) / 100m,
            runningCents >= 0 ? CreditDebitIndicator.Credit : CreditDebitIndicator.Debit,
            rangeEnd);

        // Statement number: the day-of-year of the range end — deterministic and plausibly monotonic.
        var statementNumber = rangeEnd.DayOfYear;
        var statementId = $"EBICO{rangeEnd:yyMMdd}";

        return new AccountStatement(
            account,
            rangeStart,
            rangeEnd,
            openingBalance,
            closingBalance,
            entries,
            statementId,
            statementNumber,
            SequenceNumber: 1,
            ElectronicSequenceNumber: statementNumber,
            creationTimestamp);
    }

    // A process-stable 32-bit FNV-1a hash of the subscriber triple (unit-separated). Deliberately NOT
    // string.GetHashCode(), which is randomised per process and would make the output non-reproducible.
    private static int StableSeed(string hostId, string partnerId, string userId)
    {
        var bytes = Encoding.UTF8.GetBytes($"{hostId}{partnerId}{userId}");
        var hash = 2166136261u;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return unchecked((int)hash);
    }

    // Builds a German IBAN ("DE" + 2 check digits + 18-digit BBAN) with valid ISO 13616 / ISO 7064
    // MOD 97-10 check digits, so it validates against any conformant IBAN checker.
    private static string BuildGermanIban(string bban)
    {
        // Rearrange BBAN + "DE00" and map letters D->13, E->14, then check = 98 - (value mod 97).
        var check = 98 - Mod97(bban + "131400");
        return $"DE{check:D2}{bban}";
    }

    // Computes value mod 97 of a decimal digit string iteratively (no BigInteger needed).
    private static int Mod97(string digits)
    {
        var remainder = 0;
        foreach (var c in digits)
        {
            remainder = ((remainder * 10) + (c - '0')) % 97;
        }

        return remainder;
    }
}
