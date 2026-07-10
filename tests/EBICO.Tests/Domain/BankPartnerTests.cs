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
        var partner = new Partner(HostId.Create("BANKDE01"), PartnerId.Create("PARTNER01"), "ACME");

        partner.HostId.Value.Should().Be("BANKDE01");
        partner.PartnerId.Value.Should().Be("PARTNER01");
        partner.Name.Should().Be("ACME");
    }

    [Fact]
    public void Partner_SamePartnerIdAtDifferentBanks_AreDistinct()
    {
        var atBankA = new Partner(HostId.Create("BANKA"), PartnerId.Create("CUST01"), "Kunde A");
        var atBankB = new Partner(HostId.Create("BANKB"), PartnerId.Create("CUST01"), "Kunde B");

        atBankA.PartnerId.Should().Be(atBankB.PartnerId, "the partner id string is the same");
        atBankA.HostId.Should().NotBe(atBankB.HostId, "but they belong to different banks");
    }
}
