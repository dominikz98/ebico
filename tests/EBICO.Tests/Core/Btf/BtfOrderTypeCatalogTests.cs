using AwesomeAssertions;
using EBICO.Core.Btf;

namespace EBICO.Tests.Core.Btf;

/// <summary>
/// Tests for the <see cref="BtfOrderTypeCatalog"/> (issue #38): BTF ↔ classical order-type round-trips
/// over the seeded entries, unknown lookups and the effective-order-type resolution used by the
/// transaction engines.
/// </summary>
public class BtfOrderTypeCatalogTests
{
    [Fact]
    public void All_SeedIsNonEmptyAndRoundTrips()
    {
        BtfOrderTypeCatalog.All.Should().NotBeEmpty();

        foreach (var mapping in BtfOrderTypeCatalog.All)
        {
            BtfOrderTypeCatalog.TryGetBtf(mapping.OrderType, out var btf).Should().BeTrue();
            btf.Should().Be(mapping.Btf);

            BtfOrderTypeCatalog.TryGetOrderType(mapping.Btf, out var orderType).Should().BeTrue();
            orderType.Should().Be(mapping.OrderType);
        }
    }

    [Fact]
    public void TryGetBtf_KnownUploadOrder_ResolvesService()
    {
        BtfOrderTypeCatalog.TryGetBtf("CCT", out var btf).Should().BeTrue();

        btf.Service.Should().Be("SCT");
        btf.MessageName.Should().Be("pain.001");
    }

    [Fact]
    public void TryGetBtf_Unknown_ReturnsFalse()
    {
        BtfOrderTypeCatalog.TryGetBtf("ZZZ", out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetOrderType_MatchesOnServiceOptionAndMessage()
    {
        var core = new BusinessTransactionFormat("SDD", option: "COR", messageName: "pain.008");
        var b2b = new BusinessTransactionFormat("SDD", option: "B2B", messageName: "pain.008");

        BtfOrderTypeCatalog.TryGetOrderType(core, out var coreCode).Should().BeTrue();
        coreCode.Should().Be("CDD");

        BtfOrderTypeCatalog.TryGetOrderType(b2b, out var b2bCode).Should().BeTrue();
        b2bCode.Should().Be("CDB");
    }

    [Fact]
    public void TryGetOrderType_AcceptsMessageNameFamily()
    {
        // A client sending the fully-qualified ISO name still resolves to the seeded family "camt.053".
        var btf = new BusinessTransactionFormat("EOP", container: EBICO.Core.Schema.H005.ContainerStringType.Zip, messageName: "camt.053.001.08");

        BtfOrderTypeCatalog.TryGetOrderType(btf, out var orderType).Should().BeTrue();
        orderType.Should().Be("C53");
    }

    [Fact]
    public void TryGetOrderType_UnknownService_ReturnsFalse()
    {
        BtfOrderTypeCatalog.TryGetOrderType(new BusinessTransactionFormat("XXX", messageName: "pain.001"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ResolveOrderType_WithoutBtf_UsesAdminOrderType()
    {
        // H003/H004 (FUL/FDL) and H005 requests without a BTF service fall back to the admin order type.
        BtfOrderTypeCatalog.ResolveOrderType("FUL", btf: null).Should().Be("FUL");
        BtfOrderTypeCatalog.ResolveOrderType("BTU", btf: null).Should().Be("BTU");
    }

    [Fact]
    public void ResolveOrderType_WithMappedBtf_UsesClassicalCode()
    {
        var btf = new BusinessTransactionFormat("SCT", messageName: "pain.001");

        BtfOrderTypeCatalog.ResolveOrderType("BTU", btf).Should().Be("CCT");
    }

    [Fact]
    public void ResolveOrderType_WithUnmappedBtf_FallsBackToCanonicalKey()
    {
        var btf = new BusinessTransactionFormat("XXX", messageName: "pain.999");

        BtfOrderTypeCatalog.ResolveOrderType("BTU", btf).Should().Be(btf.CanonicalKey);
    }

    [Fact]
    public void ResolveOrderType_NeitherAvailable_ReturnsNull()
    {
        BtfOrderTypeCatalog.ResolveOrderType(null, btf: null).Should().BeNull();
        BtfOrderTypeCatalog.ResolveOrderType("   ", btf: null).Should().BeNull();
    }
}
