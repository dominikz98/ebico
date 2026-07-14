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

    // --- Issue #39: CIP seed, file-format resolution, upload routing ------------------------

    [Fact]
    public void TryGetBtf_InstantCreditTransfer_ResolvesServiceWithInstOption()
    {
        BtfOrderTypeCatalog.TryGetBtf("CIP", out var btf).Should().BeTrue();

        btf.Service.Should().Be("SCT");
        btf.Option.Should().Be("INST");
        btf.MessageName.Should().Be("pain.001");
    }

    [Fact]
    public void TryGetOrderType_DistinguishesCctFromCipByOption()
    {
        var cct = new BusinessTransactionFormat("SCT", messageName: "pain.001");
        var cip = new BusinessTransactionFormat("SCT", option: "INST", messageName: "pain.001");

        BtfOrderTypeCatalog.TryGetOrderType(cct, out var cctCode).Should().BeTrue();
        cctCode.Should().Be("CCT");

        BtfOrderTypeCatalog.TryGetOrderType(cip, out var cipCode).Should().BeTrue();
        cipCode.Should().Be("CIP");
    }

    [Theory]
    [InlineData("CCT", true)]
    [InlineData("CDD", true)]
    [InlineData("CDB", true)]
    [InlineData("CIP", true)]
    [InlineData("C53", false)] // a download order type
    [InlineData("FUL", false)]
    [InlineData("HPB", false)]
    [InlineData(null, false)]
    public void IsUploadOrderType_RecognisesDirectUploadCodes(string? orderType, bool expected)
    {
        BtfOrderTypeCatalog.IsUploadOrderType(orderType).Should().Be(expected);
    }

    [Theory]
    [InlineData("pain.001.001.09", "CCT")]
    [InlineData("pain.001.001.03", "CCT")]
    [InlineData("pain.008.001.02", "CDD")] // both CDD/CDB carry pain.008; the un-optioned CDD wins
    public void TryGetOrderTypeByFileFormat_MatchesMessageFamily(string fileFormat, string expected)
    {
        BtfOrderTypeCatalog.TryGetOrderTypeByFileFormat(fileFormat, out var orderType).Should().BeTrue();
        orderType.Should().Be(expected);
    }

    [Fact]
    public void TryGetOrderTypeByFileFormat_UnknownFamily_ReturnsFalse()
    {
        BtfOrderTypeCatalog.TryGetOrderTypeByFileFormat("camt.053.001.08", out _).Should().BeFalse();
    }

    [Fact]
    public void ResolveUploadOrderType_PrefersBtf_ThenFileFormat_ThenDirectCode()
    {
        // H005 BTU with a mapped BTF.
        BtfOrderTypeCatalog
            .ResolveUploadOrderType("BTU", new BusinessTransactionFormat("SCT", messageName: "pain.001"), fileFormat: null)
            .Should().Be("CCT");

        // H003/H004 generic FUL with a file format.
        BtfOrderTypeCatalog.ResolveUploadOrderType("FUL", btf: null, fileFormat: "pain.008.001.02").Should().Be("CDD");

        // Classical order type submitted directly.
        BtfOrderTypeCatalog.ResolveUploadOrderType("CCT", btf: null, fileFormat: null).Should().Be("CCT");

        // Generic FUL without a recognised file format falls back to the raw order type.
        BtfOrderTypeCatalog.ResolveUploadOrderType("FUL", btf: null, fileFormat: null).Should().Be("FUL");
    }
}
