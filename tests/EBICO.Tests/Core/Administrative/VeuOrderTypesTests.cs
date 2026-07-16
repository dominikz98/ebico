using AwesomeAssertions;
using EBICO.Core.Administrative;

namespace EBICO.Tests.Core.Administrative;

/// <summary>
/// Unit tests for the VEU order-type classifier (issue #42): the download (HVU/HVZ/HVD/HVT) vs upload
/// (HVE/HVS) split and the negative cases.
/// </summary>
public class VeuOrderTypesTests
{
    [Theory]
    [InlineData("HVU")]
    [InlineData("HVZ")]
    [InlineData("HVD")]
    [InlineData("HVT")]
    public void IsVeuDownloadOrderType_True_ForDownloadOrders(string orderType)
    {
        VeuOrderTypes.IsVeuDownloadOrderType(orderType).Should().BeTrue();
        VeuOrderTypes.IsVeuUploadOrderType(orderType).Should().BeFalse();
        VeuOrderTypes.IsVeuOrderType(orderType).Should().BeTrue();
    }

    [Theory]
    [InlineData("HVE")]
    [InlineData("HVS")]
    public void IsVeuUploadOrderType_True_ForUploadOrders(string orderType)
    {
        VeuOrderTypes.IsVeuUploadOrderType(orderType).Should().BeTrue();
        VeuOrderTypes.IsVeuDownloadOrderType(orderType).Should().BeFalse();
        VeuOrderTypes.IsVeuOrderType(orderType).Should().BeTrue();
    }

    [Theory]
    [InlineData("CCT")]
    [InlineData("HTD")]
    [InlineData("STA")]
    [InlineData(null)]
    [InlineData("")]
    public void IsVeuOrderType_False_ForNonVeuOrders(string? orderType)
    {
        VeuOrderTypes.IsVeuOrderType(orderType).Should().BeFalse();
        VeuOrderTypes.IsVeuDownloadOrderType(orderType).Should().BeFalse();
        VeuOrderTypes.IsVeuUploadOrderType(orderType).Should().BeFalse();
    }
}
