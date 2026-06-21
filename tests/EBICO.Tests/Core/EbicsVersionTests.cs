using EBICO.Core;

namespace EBICO.Tests.Core;

/// <summary>
/// Smoke tests proving the solution skeleton compiles, references resolve and the
/// test harness runs. Real feature tests follow with their respective issues.
/// </summary>
public class EbicsVersionTests
{
    [Fact]
    public void EbicsVersion_DefinesTheThreeSupportedFamilies()
    {
        var versions = Enum.GetValues<EbicsVersion>();

        Assert.Equal(3, versions.Length);
        Assert.Contains(EbicsVersion.H003, versions);
        Assert.Contains(EbicsVersion.H004, versions);
        Assert.Contains(EbicsVersion.H005, versions);
    }

    [Theory]
    [InlineData(EbicsVersion.H003, "H003")]
    [InlineData(EbicsVersion.H004, "H004")]
    [InlineData(EbicsVersion.H005, "H005")]
    public void EbicsVersion_NameMatchesSchemaFamilyPrefix(EbicsVersion version, string expected)
    {
        Assert.Equal(expected, version.ToString());
    }
}
