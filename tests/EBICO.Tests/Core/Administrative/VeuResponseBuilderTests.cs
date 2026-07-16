using System.Text;
using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Administrative;
using EBICO.Core.Domain;

namespace EBICO.Tests.Core.Administrative;

/// <summary>
/// Unit tests for the VEU response builder (issue #42): the HVU/HVZ/HVD/HVT documents are produced in the
/// requested version's namespace and carry the order identification, signing state and signer information.
/// The three versions diverge in the order identification (H003/H004 carry the classical <c>OrderType</c>,
/// H005 the BTF <c>Service</c>), which the version-specific assertions cover.
/// </summary>
public class VeuResponseBuilderTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 7, 15, 10, 30, 0, TimeSpan.Zero);

    // A CCT (SEPA credit transfer, pain.001) order needing two signatures, one already applied by USER02.
    private static VeuOrderView SampleOrder() => new(
        OrderId: "V001",
        OrderType: "CCT",
        OrderDataSize: 1234,
        NumSigRequired: 2,
        NumSigDone: 1,
        ReadyToBeSigned: true,
        Originator: new VeuSignerView("PARTNER01", "USER01", "Alice", Stamp, Permission: null),
        Signers: [new VeuSignerView("PARTNER01", "USER02", "Bob", Stamp, SignatureClass.A)],
        DataDigest: [0x01, 0x02, 0x03, 0x04],
        AdditionalOrderInfo: null);

    [Theory]
    [InlineData(EbicsVersion.H003, "http://www.ebics.org/H003", "CCT")]
    [InlineData(EbicsVersion.H004, "urn:org:ebics:H004", "CCT")]
    [InlineData(EbicsVersion.H005, "urn:org:ebics:H005", "pain.001")]
    public void BuildHvu_RendersOverview_AcrossVersions(EbicsVersion version, string ns, string serviceToken)
    {
        var xml = Text(VeuResponseBuilder.BuildHvu(version, [SampleOrder()]));

        xml.Should().Contain("HVUResponseOrderData").And.Contain(ns);
        xml.Should().Contain("V001");
        xml.Should().Contain("NumSigRequired").And.Contain("NumSigDone");
        xml.Should().Contain(serviceToken);
        // The originator and the existing signer are both surfaced.
        xml.Should().Contain("USER01").And.Contain("USER02");
    }

    [Fact]
    public void BuildHvu_EmptyList_ProducesEmptyOverviewDocument()
    {
        var xml = Text(VeuResponseBuilder.BuildHvu(EbicsVersion.H005, []));

        xml.Should().Contain("HVUResponseOrderData");
        xml.Should().NotContain("OrderID");
    }

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildHvz_RendersOverviewWithDetails(EbicsVersion version)
    {
        var xml = Text(VeuResponseBuilder.BuildHvz(version, [SampleOrder()]));

        xml.Should().Contain("HVZResponseOrderData").And.Contain("V001").And.Contain("DataDigest");
    }

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildHvd_RendersStatusWithDigestAndSigners(EbicsVersion version)
    {
        var xml = Text(VeuResponseBuilder.BuildHvd(version, SampleOrder()));

        xml.Should().Contain("HVDResponseOrderData").And.Contain("DataDigest");
        xml.Should().Contain("USER02");
    }

    [Theory]
    [InlineData(EbicsVersion.H003)]
    [InlineData(EbicsVersion.H004)]
    [InlineData(EbicsVersion.H005)]
    public void BuildHvt_RendersTransactionSummary(EbicsVersion version)
    {
        var xml = Text(VeuResponseBuilder.BuildHvt(version, SampleOrder()));

        xml.Should().Contain("HVTResponseOrderData").And.Contain("NumOrderInfos");
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
