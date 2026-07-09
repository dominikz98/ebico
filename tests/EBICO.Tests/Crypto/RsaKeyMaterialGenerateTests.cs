using AwesomeAssertions;
using EBICO.Core.Crypto;

namespace EBICO.Tests.Crypto;

/// <summary>Tests for the client-side RSA key generation added for onboarding (issue #47).</summary>
public class RsaKeyMaterialGenerateTests
{
    [Fact]
    public void Generate_Default_ProducesPrivateKeyAtMinimumSize()
    {
        var material = RsaKeyMaterial.Generate();

        material.HasPrivateKey.Should().BeTrue();
        material.KeySizeBits.Should().Be(RsaKeyMaterial.MinKeySizeBits);
    }

    [Fact]
    public void Generate_LargerSize_IsHonoured()
    {
        var material = RsaKeyMaterial.Generate(3072);

        material.KeySizeBits.Should().Be(3072);
    }

    [Fact]
    public void Generate_BelowMinimum_Throws()
    {
        var act = () => RsaKeyMaterial.Generate(1024);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void Generate_TwoCalls_ProduceDifferentKeys()
    {
        var first = RsaKeyMaterial.Generate();
        var second = RsaKeyMaterial.Generate();

        first.Modulus.ToArray().Should().NotEqual(second.Modulus.ToArray());
    }

    [Fact]
    public void Generate_RoundTripsThroughPkcs8()
    {
        var material = RsaKeyMaterial.Generate();

        var reimported = RsaKeyImportExport.ImportPkcs8(RsaKeyImportExport.ExportPkcs8(material));

        reimported.Modulus.ToArray().Should().Equal(material.Modulus.ToArray());
        reimported.Exponent.ToArray().Should().Equal(material.Exponent.ToArray());
    }
}
