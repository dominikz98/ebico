using AwesomeAssertions;
using EBICO.Core.Btf;

namespace EBICO.Tests.Core.Btf;

/// <summary>
/// Tests for the download-side additions to <see cref="BtfOrderTypeCatalog"/> (issue #40): the VMK/mt942
/// mapping, <see cref="BtfOrderTypeCatalog.IsDownloadOrderType"/> and
/// <see cref="BtfOrderTypeCatalog.ResolveDownloadOrderType"/> across BTF, FileFormat and direct-code inputs.
/// </summary>
public class BtfDownloadOrderTypeTests
{
    [Theory]
    [InlineData("STA")]
    [InlineData("VMK")]
    [InlineData("C53")]
    [InlineData("C52")]
    [InlineData("C54")]
    public void IsDownloadOrderType_ForStatementCodes_IsTrue(string orderType)
        => BtfOrderTypeCatalog.IsDownloadOrderType(orderType).Should().BeTrue();

    [Theory]
    [InlineData("CCT")]
    [InlineData("FDL")]
    [InlineData(null)]
    public void IsDownloadOrderType_ForNonDownloadCodes_IsFalse(string? orderType)
        => BtfOrderTypeCatalog.IsDownloadOrderType(orderType).Should().BeFalse();

    [Fact]
    public void TryGetBtf_Vmk_MapsToMt942InterimReport()
    {
        BtfOrderTypeCatalog.TryGetBtf("VMK", out var btf).Should().BeTrue();
        btf.Service.Should().Be("STM");
        btf.MessageName.Should().Be("mt942");
    }

    [Fact]
    public void ResolveDownloadOrderType_FromBtf_ResolvesCamt053ToC53()
    {
        var btf = new BusinessTransactionFormat("EOP", messageName: "camt.053");

        BtfOrderTypeCatalog.ResolveDownloadOrderType("BTD", btf, null).Should().Be("C53");
    }

    [Fact]
    public void ResolveDownloadOrderType_FromFileFormat_ResolvesMt940ToSta()
        => BtfOrderTypeCatalog.ResolveDownloadOrderType("FDL", null, "mt940").Should().Be("STA");

    [Fact]
    public void ResolveDownloadOrderType_FromFileFormat_ResolvesMt942ToVmk()
        => BtfOrderTypeCatalog.ResolveDownloadOrderType("FDL", null, "mt942").Should().Be("VMK");

    [Fact]
    public void ResolveDownloadOrderType_DirectCode_FallsBackToRaw()
        => BtfOrderTypeCatalog.ResolveDownloadOrderType("STA", null, null).Should().Be("STA");

    [Fact]
    public void TryGetOrderTypeByFileFormat_Download_DistinguishesMt942FromCamt052()
    {
        BtfOrderTypeCatalog.TryGetOrderTypeByFileFormat("mt942", out var vmk, BtfDirection.Download).Should().BeTrue();
        vmk.Should().Be("VMK");

        BtfOrderTypeCatalog.TryGetOrderTypeByFileFormat("camt.052", out var c52, BtfDirection.Download).Should().BeTrue();
        c52.Should().Be("C52");
    }
}
