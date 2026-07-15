using System.Text;
using AwesomeAssertions;
using EBICO.Core.Statements;

namespace EBICO.Tests.Core.Statements;

/// <summary>Tests for <see cref="Mt940Builder"/> (STA): tag presence, comma-decimal amounts, empty range.</summary>
public class Mt940BuilderTests
{
    [Fact]
    public void Build_WithEntries_EmitsExpectedTags()
    {
        var text = Encoding.UTF8.GetString(Mt940Builder.Build(StatementSampleData.WithTwoEntries()));

        text.Should().Contain(":20:EBICO260731");
        text.Should().Contain(":25:DE02120300000000202051");
        text.Should().Contain(":28C:195/1");
        text.Should().Contain(":60F:C260701EUR1000,00");
        text.Should().Contain(":61:2607020702C200,00NTRFREF000001");
        text.Should().Contain(":86:Rechnung 1 Kunde AG");
        text.Should().Contain(":62F:C260731EUR1150,50");
        text.Should().EndWith("\r\n");
    }

    [Fact]
    public void Build_EmptyRange_HasEqualBalances_AndNoEntryTags()
    {
        var text = Encoding.UTF8.GetString(Mt940Builder.Build(StatementSampleData.Empty()));

        text.Should().Contain(":60F:C260701EUR1000,00");
        text.Should().Contain(":62F:C260731EUR1000,00");
        text.Should().NotContain(":61:");
        text.Should().NotContain(":86:");
    }
}
