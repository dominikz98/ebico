using AwesomeAssertions;
using EBICO.Core.Btf;
using EBICO.Core.Schema.H005;

namespace EBICO.Tests.Core.Btf;

/// <summary>
/// Tests for the <see cref="BusinessTransactionFormat"/> value object (issue #38): construction and
/// validation, projection to/from the generated <see cref="ServiceType"/> binding, the canonical key
/// and value equality.
/// </summary>
public class BusinessTransactionFormatTests
{
    [Fact]
    public void Ctor_KeepsAllComponents()
    {
        var btf = new BusinessTransactionFormat(
            service: "SDD",
            option: "COR",
            scope: "DE",
            container: ContainerStringType.Zip,
            messageName: "pain.008",
            messageVariant: "001",
            messageVersion: "08",
            messageFormat: "XML");

        btf.Service.Should().Be("SDD");
        btf.Option.Should().Be("COR");
        btf.Scope.Should().Be("DE");
        btf.Container.Should().Be(ContainerStringType.Zip);
        btf.MessageName.Should().Be("pain.008");
        btf.MessageVariant.Should().Be("001");
        btf.MessageVersion.Should().Be("08");
        btf.MessageFormat.Should().Be("XML");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_RejectsMissingService(string? service)
    {
        var act = () => new BusinessTransactionFormat(service!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CanonicalKey_JoinsNonEmptyComponents()
    {
        var btf = new BusinessTransactionFormat("SCT", option: "COR", messageName: "pain.001");

        btf.CanonicalKey.Should().Be("SCT:pain.001:COR");
    }

    [Fact]
    public void CanonicalKey_ServiceOnly_IsService()
    {
        new BusinessTransactionFormat("SCT").CanonicalKey.Should().Be("SCT");
    }

    [Fact]
    public void FromSchema_ReadsServiceAndMessage()
    {
        var service = new ServiceType
        {
            ServiceName = "SCT",
            ServiceOption = "COR",
            Scope = "DE",
            MsgName = new MessageType { Value = "pain.001", Variant = "001", Version = "09", Format = "XML" },
        };

        var btf = BusinessTransactionFormat.FromSchema(service);

        btf.Service.Should().Be("SCT");
        btf.Option.Should().Be("COR");
        btf.Scope.Should().Be("DE");
        btf.MessageName.Should().Be("pain.001");
        btf.MessageVariant.Should().Be("001");
        btf.MessageVersion.Should().Be("09");
        btf.MessageFormat.Should().Be("XML");
    }

    [Fact]
    public void FromSchema_NormalizesEmptyStringsToNull()
    {
        var service = new ServiceType { ServiceName = "EOP", ServiceOption = "", Scope = "" };

        var btf = BusinessTransactionFormat.FromSchema(service);

        btf.Option.Should().BeNull();
        btf.Scope.Should().BeNull();
        btf.MessageName.Should().BeNull();
    }

    [Fact]
    public void ToServiceType_RoundTripsCoreComponents()
    {
        var original = new BusinessTransactionFormat(
            service: "SDD",
            option: "B2B",
            scope: "DE",
            messageName: "pain.008",
            messageVariant: "001",
            messageVersion: "08",
            messageFormat: "XML");

        var roundTripped = BusinessTransactionFormat.FromSchema(original.ToServiceType());

        // Container is deliberately excluded — its value is not round-tripped through the schema flag
        // attribute (see docs/server/btf-framework.md, Spec-Vorbehalt).
        roundTripped.Should().Be(original);
    }

    [Fact]
    public void ToRestrictedServiceType_ProducesRestrictedBinding()
    {
        var service = new BusinessTransactionFormat("SCT", messageName: "pain.001").ToRestrictedServiceType();

        service.Should().BeOfType<RestrictedServiceType>();
        service.ServiceName.Should().Be("SCT");
        service.MsgName.Value.Should().Be("pain.001");
    }

    [Fact]
    public void TryFromBtfParams_TrueForServiceWithName()
    {
        var btfParams = new BtuParamsType { Service = new BusinessTransactionFormat("SCT", messageName: "pain.001").ToRestrictedServiceType() };

        BusinessTransactionFormat.TryFromBtfParams(btfParams, out var btf).Should().BeTrue();
        btf.Service.Should().Be("SCT");
        btf.MessageName.Should().Be("pain.001");
    }

    [Fact]
    public void TryFromBtfParams_FalseForNullOrEmptyService()
    {
        BusinessTransactionFormat.TryFromBtfParams(null, out _).Should().BeFalse();
        BusinessTransactionFormat.TryFromBtfParams(new BtdParamsType(), out _).Should().BeFalse();
        BusinessTransactionFormat.TryFromBtfParams(new BtdParamsType { Service = new RestrictedServiceType { ServiceName = "" } }, out _).Should().BeFalse();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new BusinessTransactionFormat("SDD", option: "COR", messageName: "pain.008");
        var b = new BusinessTransactionFormat("SDD", option: "COR", messageName: "pain.008");
        var c = new BusinessTransactionFormat("SDD", option: "B2B", messageName: "pain.008");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
