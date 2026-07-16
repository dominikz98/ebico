using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using EBICO.Core.Administrative;
using EBICO.Core.Btf;
using EBICO.Core.Payments;
using EBICO.Core.Statements;
using EBICO.Server.Handlers;
using EBICO.Server.Transactions;

namespace EBICO.Tests.Docs;

/// <summary>
/// Guard tests for the order-/BTF coverage matrix (<c>docs/server/order-coverage-matrix.md</c>, issue #43).
/// They keep the hand-written matrix in sync with the code: every order-type code registered in the code
/// catalogs must appear in the matrix, so no implemented order can silently drop out of the documentation.
/// The matrix may additionally list planned/not-yet-implemented codes (e.g. <c>Z53</c>, <c>HEV</c>,
/// <c>H3K</c>), so the check is one-directional (matrix ⊇ code).
/// </summary>
public class OrderCoverageMatrixTests
{
    // A classical EBICS order-type code is exactly three upper-case letters/digits (e.g. CCT, STA, C53).
    private static readonly Regex OrderCode = new("^[A-Z0-9]{3}$", RegexOptions.Compiled);

    private static readonly string MatrixText = File.ReadAllText(MatrixPath());

    [Fact]
    public void Matrix_ContainsEveryOrderTypeRegisteredInCode()
    {
        var expected = ExpectedOrderCodes();
        var documented = DocumentedOrderCodes();

        var missing = expected.Except(documented).OrderBy(code => code, StringComparer.Ordinal).ToList();

        missing.Should().BeEmpty(
            "every order type registered in the code catalogs must be listed in the coverage matrix "
            + "(docs/server/order-coverage-matrix.md); add a row for each missing code");
    }

    [Fact]
    public void Matrix_ListsTheVersionSpecificGrenzfaelle()
    {
        // HSA is H003/H004 only (removed in H005); BTU/BTD are the H005-only generic carriers. All three
        // must be present so the version columns tell the full story.
        var documented = DocumentedOrderCodes();

        documented.Should().Contain(HsaOrderHandlerBase.HsaOrderType);
        documented.Should().Contain(UploadTransactionEngine.BtuOrderType);
        documented.Should().Contain(DownloadTransactionEngine.BtdOrderType);
    }

    [Theory]
    [InlineData("## Legende")]
    [InlineData("## Offene Lücken")]
    [InlineData("## EBICS-Versionsbezug")]
    public void Matrix_HasRequiredSections(string heading)
    {
        MatrixText.Should().Contain(heading);
    }

    /// <summary>The set of order-type codes actually registered across the code catalogs (the source of truth).</summary>
    private static HashSet<string> ExpectedOrderCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        // BTF ↔ classical-code catalog (payments + statements).
        foreach (var mapping in BtfOrderTypeCatalog.All)
        {
            codes.Add(mapping.OrderType);
        }

        // The per-family static order-type catalogs (their string constants).
        foreach (var catalog in new[]
                 {
                     typeof(PaymentOrderTypes),
                     typeof(StatementOrderTypes),
                     typeof(StatusProtocolOrderTypes),
                     typeof(VeuOrderTypes),
                 })
        {
            foreach (var value in StringConstantsOf(catalog))
            {
                codes.Add(value);
            }
        }

        // Key-management handler and generic transport-engine constants (Server).
        codes.Add(IniOrderHandlerBase.IniOrderType);
        codes.Add(HiaOrderHandlerBase.HiaOrderType);
        codes.Add(HpbOrderHandlerBase.HpbOrderType);
        codes.Add(HsaOrderHandlerBase.HsaOrderType);
        codes.Add(HcaOrderHandlerBase.HcaOrderType);
        codes.Add(HcsOrderHandlerBase.HcsOrderType);
        codes.Add(SprOrderHandlerBase.SprOrderType);
        codes.Add(UploadTransactionEngine.FulOrderType);
        codes.Add(UploadTransactionEngine.BtuOrderType);
        codes.Add(DownloadTransactionEngine.FdlOrderType);
        codes.Add(DownloadTransactionEngine.BtdOrderType);

        // Keep only three-character order codes; drops non-code constants such as the "pain.001"/"pain.008"
        // message families exposed by PaymentOrderTypes.
        codes.RemoveWhere(code => !OrderCode.IsMatch(code));
        return codes;
    }

    /// <summary>The order-type codes found in the first column of the matrix's markdown tables.</summary>
    private static HashSet<string> DocumentedOrderCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in MatrixText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            // "| `CCT` | … |" → cells ["", " `CCT` ", " … ", ""]; the first data cell is index 1.
            var cells = line.Split('|');
            if (cells.Length < 2)
            {
                continue;
            }

            var code = cells[1].Trim().Trim('`').Trim();
            if (OrderCode.IsMatch(code))
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    private static IEnumerable<string> StringConstantsOf(Type type)
        => type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static string MatrixPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EBICO.sln")))
            {
                return Path.Combine(directory.FullName, "docs", "server", "order-coverage-matrix.md");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (no EBICO.sln found walking up from "
            + $"'{AppContext.BaseDirectory}').");
    }
}
