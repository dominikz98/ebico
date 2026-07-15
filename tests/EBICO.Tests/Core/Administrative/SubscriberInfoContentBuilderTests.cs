using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Administrative;
using EBICO.Core.Domain;
using EBICO.Core.Serialization;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Core.Administrative;

/// <summary>
/// Tests for <see cref="SubscriberInfoContentBuilder"/> (issue #41): the HTD/HKD/HAA/HPD response order data
/// is populated from the domain aggregates and serialised into the per-version bindings. The produced XML is
/// deserialised back into the generated bindings (round-trip) to assert the mapping.
/// </summary>
public class SubscriberInfoContentBuilderTests
{
    private const string Iban = "DE89370400440532013000";
    private const string Bic = "COBADEFFXXX";

    private static Bank Bank() => new(HostId.Create("HOST01"), "EBICO Test Bank", url: "https://ebico.example/ebics");

    private static Partner Partner() => new(
        HostId.Create("HOST01"),
        PartnerId.Create("PARTNER1"),
        "Acme GmbH",
        new Address("Acme GmbH", "Hauptstr. 1", "10115", "Berlin", "BE", "DE"),
        [new BankAccount(Iban, Bic, "Acme GmbH", "EUR", "Main account", "ACC1")]);

    private static Subscriber User(string userId = "USER01", string name = "Alice") => new(
        HostId.Create("HOST01"),
        PartnerId.Create("PARTNER1"),
        UserId.Create(userId),
        permissions: [new SubscriberPermission("C53", SignatureClass.T), new SubscriberPermission("HTD", SignatureClass.T)],
        name: name);

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildHtd_ContainsCoreDataAcrossVersions(EbicsVersion version)
    {
        var xml = Encoding.UTF8.GetString(SubscriberInfoContentBuilder.BuildHtd(version, Bank(), Partner(), User()));

        xml.Should().Contain("HTDResponseOrderData");
        xml.Should().Contain("USER01").And.Contain("Alice");
        xml.Should().Contain("Acme GmbH").And.Contain("Berlin").And.Contain(Iban);
    }

    [Fact]
    public void BuildHtd_H005_RoundTripsPartnerAndUserInfo()
    {
        var xml = Encoding.UTF8.GetString(SubscriberInfoContentBuilder.BuildHtd(EbicsVersion.H005, Bank(), Partner(), User()));
        var htd = EbicsXmlSerializer.Deserialize<H005.HtdReponseOrderDataType>(xml);

        htd.PartnerInfo.Should().NotBeNull();
        htd.PartnerInfo.AddressInfo!.Name.Should().Be("Acme GmbH");
        htd.PartnerInfo.AddressInfo.City.Should().Be("Berlin");
        htd.PartnerInfo.BankInfo!.HostId.Should().Be("HOST01");

        var account = htd.PartnerInfo.AccountInfo.Should().ContainSingle().Subject;
        account.Id.Should().Be("ACC1");
        account.AccountNumber.Should().ContainSingle().Which.Value.Should().Be(Iban);
        account.BankCode.Should().ContainSingle().Which.Value.Should().Be(Bic);

        // C53 maps to a BTF service; HTD stays an admin order type.
        htd.PartnerInfo.OrderInfo.Should().Contain(o => o.AdminOrderType == "HTD");
        htd.PartnerInfo.OrderInfo.Should().Contain(o => o.Service != null && o.Service.ServiceName == "EOP");

        htd.UserInfo.UserId!.Value.Should().Be("USER01");
        htd.UserInfo.Name.Should().Be("Alice");
        htd.UserInfo.Permission.Should().HaveCount(2);
    }

    [Fact]
    public void BuildHkd_H005_ListsAllUsers()
    {
        var xml = Encoding.UTF8.GetString(
            SubscriberInfoContentBuilder.BuildHkd(EbicsVersion.H005, Bank(), Partner(), [User("USER01", "Alice"), User("USER02", "Bob")]));
        var hkd = EbicsXmlSerializer.Deserialize<H005.HkdResponseOrderDataType>(xml);

        hkd.PartnerInfo.Should().NotBeNull();
        hkd.UserInfo.Should().HaveCount(2);
        hkd.UserInfo.Select(u => u.UserId!.Value).Should().BeEquivalentTo(["USER01", "USER02"]);
    }

    [Fact]
    public void BuildHaa_H005_ListsBtfServices()
    {
        var xml = Encoding.UTF8.GetString(SubscriberInfoContentBuilder.BuildHaa(EbicsVersion.H005, ["C53"]));
        var haa = EbicsXmlSerializer.Deserialize<H005.HaaResponseOrderDataType>(xml);

        haa.Service.Should().ContainSingle().Which.ServiceName.Should().Be("EOP");
    }

    [Fact]
    public void BuildHaa_H004_ListsClassicalOrderTypeCodes()
    {
        var xml = Encoding.UTF8.GetString(SubscriberInfoContentBuilder.BuildHaa(EbicsVersion.H004, ["C53", "STA"]));
        var haa = EbicsXmlSerializer.Deserialize<H004.HaaResponseOrderDataType>(xml);

        haa.OrderTypes.Should().BeEquivalentTo(["C53", "STA"]);
    }

    [Fact]
    public void BuildHpd_H005_ReturnsAccessAndProtocolParameters()
    {
        var xml = Encoding.UTF8.GetString(SubscriberInfoContentBuilder.BuildHpd(EbicsVersion.H005, Bank()));
        var hpd = EbicsXmlSerializer.Deserialize<H005.HpdResponseOrderDataType>(xml);

        hpd.AccessParams!.HostId.Should().Be("HOST01");
        hpd.AccessParams.Institute.Should().Be("EBICO Test Bank");
        hpd.AccessParams.Url.Should().ContainSingle().Which.Value.Should().Be("https://ebico.example/ebics");

        hpd.ProtocolParams!.Version!.Protocol.Should().Contain("H005");
        hpd.ProtocolParams.Version.Encryption.Should().Contain("E002");
        hpd.ProtocolParams.ClientDataDownload!.Supported.Should().BeTrue();
    }

    [Fact]
    public void BuildHpd_H003_RoundTrips()
    {
        var xml = Encoding.UTF8.GetString(SubscriberInfoContentBuilder.BuildHpd(EbicsVersion.H003, Bank()));
        var hpd = EbicsXmlSerializer.Deserialize<H003.HpdResponseOrderDataType>(xml);

        hpd.AccessParams!.HostId.Should().Be("HOST01");
        hpd.ProtocolParams!.Version!.Protocol.Should().Contain("H003");
    }
}
