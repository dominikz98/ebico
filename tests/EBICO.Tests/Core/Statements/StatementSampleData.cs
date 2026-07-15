using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>
/// Shared, hand-built <see cref="AccountStatement"/> fixtures for the statement-builder tests: fixed,
/// human-readable values so each format's exact output can be asserted (issue #40).
/// </summary>
internal static class StatementSampleData
{
    public static readonly DateOnly RangeStart = new(2026, 7, 1);
    public static readonly DateOnly RangeEnd = new(2026, 7, 31);
    public static readonly DateTimeOffset Created = new(2026, 7, 31, 12, 0, 0, TimeSpan.FromHours(2));

    public static StatementAccount Account { get; } =
        new("DE02120300000000202051", "EBICDEMMXXX", "EUR", "Muster GmbH", "0000202051");

    public static IReadOnlyList<StatementEntry> TwoEntries { get; } =
    [
        new StatementEntry(new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 2), 200.00m, CreditDebitIndicator.Credit, "Rechnung 1", "Kunde AG", "DE29100500000054540402", "E2E-000001", "REF000001"),
        new StatementEntry(new DateOnly(2026, 7, 3), new DateOnly(2026, 7, 3), 49.50m, CreditDebitIndicator.Debit, "Abschlag 2", "Stadtwerke", "DE29100500000054540402", "E2E-000002", "REF000002"),
    ];

    public static AccountStatement Build(IReadOnlyList<StatementEntry> entries, decimal openingAmount, decimal closingAmount)
        => new(
            Account,
            RangeStart,
            RangeEnd,
            new StatementBalance(StatementBalanceType.Opening, openingAmount, CreditDebitIndicator.Credit, RangeStart),
            new StatementBalance(StatementBalanceType.Closing, closingAmount, CreditDebitIndicator.Credit, RangeEnd),
            entries,
            "EBICO260731",
            195,
            1,
            195,
            Created);

    // Two entries: opening 1000.00, closing 1000.00 + 200.00 − 49.50 = 1150.50.
    public static AccountStatement WithTwoEntries() => Build(TwoEntries, 1000.00m, 1150.50m);

    // Movement-free statement: opening == closing, no entries.
    public static AccountStatement Empty() => Build([], 1000.00m, 1000.00m);
}
