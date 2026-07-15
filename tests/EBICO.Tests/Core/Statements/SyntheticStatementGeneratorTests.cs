using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>
/// Tests for <see cref="SyntheticStatementGenerator"/> (issue #40): determinism, date-range containment,
/// the opening/closing balance invariant, IBAN validity and input validation.
/// </summary>
public class SyntheticStatementGeneratorTests
{
    private static readonly DateOnly Start = new(2026, 6, 1);
    private static readonly DateOnly End = new(2026, 6, 30);
    private static readonly DateTimeOffset Created = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_SameInputs_IsDeterministic()
    {
        var a = SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER01", Start, End, Created);
        var b = SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER01", Start, End, Created);

        a.Account.Should().Be(b.Account);
        a.OpeningBalance.Should().Be(b.OpeningBalance);
        a.ClosingBalance.Should().Be(b.ClosingBalance);
        a.Entries.Should().Equal(b.Entries);
    }

    [Fact]
    public void Generate_AllBookingDates_WithinRange()
    {
        var statement = SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER01", Start, End, Created);

        statement.Entries.Should().OnlyContain(e => e.BookingDate >= Start && e.BookingDate <= End);
        statement.OpeningBalance.Date.Should().Be(Start);
        statement.ClosingBalance.Date.Should().Be(End);
    }

    [Fact]
    public void Generate_ClosingBalance_EqualsOpeningPlusMovements()
    {
        var statement = SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER01", Start, End, Created);

        var openingSigned = Signed(statement.OpeningBalance.CreditDebit, statement.OpeningBalance.Amount);
        var movements = statement.Entries.Sum(e => Signed(e.CreditDebit, e.Amount));
        var closingSigned = Signed(statement.ClosingBalance.CreditDebit, statement.ClosingBalance.Amount);

        closingSigned.Should().Be(openingSigned + movements);
    }

    [Fact]
    public void Generate_Account_HasValidGermanIban()
    {
        var statement = SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER01", Start, End, Created);

        statement.Account.Iban.Should().StartWith("DE").And.HaveLength(22);
        IbanChecksumValid(statement.Account.Iban).Should().BeTrue();
        statement.Account.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Generate_DifferentSubscribers_ProduceDifferentAccounts()
    {
        var a = SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER01", Start, End, Created);
        var b = SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER02", Start, End, Created);

        a.Account.Iban.Should().NotBe(b.Account.Iban);
    }

    [Fact]
    public void Generate_EndBeforeStart_Throws()
    {
        var act = () => SyntheticStatementGenerator.Generate("EBICOHOST", "PARTNER01", "USER01", End, Start, Created);

        act.Should().Throw<ArgumentException>();
    }

    private static decimal Signed(CreditDebitIndicator indicator, decimal amount)
        => indicator == CreditDebitIndicator.Credit ? amount : -amount;

    // Validates the ISO 7064 MOD 97-10 IBAN checksum: rearrange (BBAN + country + check), map letters to
    // digits (A=10..Z=35) and assert value mod 97 == 1.
    private static bool IbanChecksumValid(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var value = char.IsDigit(c) ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
            remainder = value > 9 ? ((remainder * 100) + value) % 97 : ((remainder * 10) + value) % 97;
        }

        return remainder == 1;
    }
}
