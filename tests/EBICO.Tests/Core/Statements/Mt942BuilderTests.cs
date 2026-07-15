using System.Text;
using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>Tests for <see cref="Mt942Builder"/> (VMK): interim tags, debit/credit summaries, no booked balances.</summary>
public class Mt942BuilderTests
{
    [Fact]
    public void Build_EmitsInterimTags_AndSummaries()
    {
        var text = Encoding.UTF8.GetString(Mt942Builder.Build(StatementSampleData.WithTwoEntries()));

        text.Should().Contain(":20:EBICO260731");
        text.Should().Contain(":25:DE02120300000000202051");
        text.Should().Contain(":34F:EUR0,00");
        text.Should().Contain(":13D:2607311200+0200");
        text.Should().Contain(":90D:1EUR49,50");
        text.Should().Contain(":90C:1EUR200,00");
    }

    [Fact]
    public void Build_HasNoBookedOpeningOrClosingBalance()
    {
        var text = Encoding.UTF8.GetString(Mt942Builder.Build(StatementSampleData.WithTwoEntries()));

        text.Should().NotContain(":60F:");
        text.Should().NotContain(":62F:");
    }
}
