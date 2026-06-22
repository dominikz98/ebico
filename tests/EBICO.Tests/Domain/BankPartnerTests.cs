using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Domain;

namespace EBICO.Tests.Domain;

/// <summary>
/// Tests for the lightweight <see cref="Bank"/> and <see cref="Partner"/> aggregates
/// (issue #16). Tier A.
/// </summary>
public class BankPartnerTests
{
    [Fact]
    public void Bank_DefaultsToAllSupportedVersions()
    {
        var bank = new Bank(HostId.Create("BANKDE01"));

        bank.HostId.Value.Should().Be("BANKDE01");
        bank.Name.Should().BeNull();

        var expected = new[] { EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005 };
        bank.SupportedVersions.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Bank_HonorsExplicitVersionsAndName_AndCollapsesDuplicates()
    {
        var versions = new[] { EbicsVersion.H005, EbicsVersion.H005 };

        var bank = new Bank(HostId.Create("BANKDE01"), "Test Bank", versions);

        bank.Name.Should().Be("Test Bank");
        bank.SupportedVersions.Should().ContainSingle().Which.Should().Be(EbicsVersion.H005);
    }

    [Fact]
    public void Partner_HoldsIdentityAndOptionalName()
    {
        var partner = new Partner(PartnerId.Create("PARTNER01"), "ACME");

        partner.PartnerId.Value.Should().Be("PARTNER01");
        partner.Name.Should().Be("ACME");
    }
}
