using AwesomeAssertions;
using EBICO.Core.Administrative;

namespace EBICO.Tests.Core.Administrative;

/// <summary>Tests for the <see cref="StatusProtocolOrderTypes"/> classification helpers (issue #41).</summary>
public class StatusProtocolOrderTypesTests
{
    [Theory]
    [InlineData("HTD")]
    [InlineData("HKD")]
    [InlineData("HAA")]
    [InlineData("HPD")]
    [InlineData("HAC")]
    [InlineData("PTK")]
    public void IsStatusProtocolOrderType_True_ForAdminOrders(string orderType)
        => StatusProtocolOrderTypes.IsStatusProtocolOrderType(orderType).Should().BeTrue();

    [Theory]
    [InlineData("STA")]
    [InlineData("C53")]
    [InlineData("CCT")]
    [InlineData("BTD")]
    [InlineData(null)]
    public void IsStatusProtocolOrderType_False_ForOtherOrders(string? orderType)
        => StatusProtocolOrderTypes.IsStatusProtocolOrderType(orderType).Should().BeFalse();

    [Theory]
    [InlineData("HTD", true)]
    [InlineData("HKD", true)]
    [InlineData("HAA", true)]
    [InlineData("HPD", true)]
    [InlineData("HAC", false)]
    [InlineData("PTK", false)]
    public void IsSubscriberInfoOrderType_ClassifiesParameterOrders(string orderType, bool expected)
        => StatusProtocolOrderTypes.IsSubscriberInfoOrderType(orderType).Should().Be(expected);

    [Theory]
    [InlineData("HAC", true)]
    [InlineData("PTK", true)]
    [InlineData("HTD", false)]
    [InlineData("STA", false)]
    public void IsCustomerProtocolOrderType_ClassifiesProtocolOrders(string orderType, bool expected)
        => StatusProtocolOrderTypes.IsCustomerProtocolOrderType(orderType).Should().Be(expected);
}
