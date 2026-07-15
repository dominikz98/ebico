using AwesomeAssertions;
using EBICO.Core.Domain;

namespace EBICO.Tests.Domain;

/// <summary>
/// Tests for the domain-model extensions carrying the customer/subscriber data surfaced by HTD/HKD/HPD
/// (issue #41): <see cref="Partner.Address"/>/<see cref="Partner.Accounts"/>, <see cref="Bank.Url"/> and
/// <see cref="Subscriber.Name"/> (including its preservation across the immutable copy operations).
/// </summary>
public class SubscriberInfoDomainTests
{
    private static readonly HostId Host = HostId.Create("HOST01");
    private static readonly PartnerId Partner = PartnerId.Create("PARTNER1");
    private static readonly UserId User = UserId.Create("USER01");

    [Fact]
    public void Partner_CarriesAddressAndAccounts()
    {
        var address = new Address("Acme GmbH", City: "Berlin");
        var account = new BankAccount("DE89370400440532013000", "COBADEFFXXX", Id: "ACC1");

        var partner = new Partner(Host, Partner, "Acme GmbH", address, [account]);

        partner.Address.Should().Be(address);
        partner.Accounts.Should().ContainSingle().Which.Should().Be(account);
    }

    [Fact]
    public void Partner_WithoutAddressOrAccounts_DefaultsToNullAndEmpty()
    {
        var partner = new Partner(Host, Partner);

        partner.Address.Should().BeNull();
        partner.Accounts.Should().BeEmpty();
    }

    [Fact]
    public void BankAccount_DefaultsCurrencyToEur()
        => new BankAccount(Iban: "DE89370400440532013000").Currency.Should().Be("EUR");

    [Fact]
    public void Bank_CarriesUrl()
        => new Bank(Host, "Test Bank", url: "https://ebico.example/ebics").Url.Should().Be("https://ebico.example/ebics");

    [Fact]
    public void Subscriber_CarriesName()
        => new Subscriber(Host, Partner, User, name: "Alice").Name.Should().Be("Alice");

    [Fact]
    public void Subscriber_Name_SurvivesTransition()
    {
        var subscriber = new Subscriber(Host, Partner, User, state: SubscriberState.New, name: "Alice");

        subscriber.Transition(SubscriberState.Initialized).Name.Should().Be("Alice");
    }

    [Fact]
    public void Subscriber_Name_SurvivesPermissionMutations()
    {
        var subscriber = new Subscriber(Host, Partner, User, name: "Alice");
        var permission = new SubscriberPermission("HTD", SignatureClass.T);

        var withPermission = subscriber.WithPermission(permission);
        withPermission.Name.Should().Be("Alice");
        withPermission.WithoutPermissionsFor("HTD").Name.Should().Be("Alice");
        subscriber.WithPermissions([permission]).Name.Should().Be("Alice");
    }
}
