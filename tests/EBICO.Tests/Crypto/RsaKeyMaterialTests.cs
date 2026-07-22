using System.Security.Cryptography;
using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="RsaKeyMaterial"/> — capture of public/private material, the minimum
/// key-size policy, canonical modulus form, and defensive immutability (issue #18). Tier A —
/// keys are generated in-process via <see cref="TestCertificates"/>; no proprietary samples.
/// </summary>
public class RsaKeyMaterialTests
{
    [Fact]
    public void FromKeyPair_CapturesPrivateKeyAndSize()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);

        var material = RsaKeyMaterial.FromKeyPair(rsa);

        material.HasPrivateKey.Should().BeTrue();
        material.KeySizeBits.Should().Be(2048);
        material.Modulus.Length.Should().Be(256);
        material.Exponent.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FromPublicKey_HasNoPrivateKey()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);

        RsaKeyMaterial.FromPublicKey(rsa).HasPrivateKey.Should().BeFalse();
    }

    [Fact]
    public void FromKeyPair_PublicOnlyRsa_Throws()
    {
        using var source = TestCertificates.CreateRsaKey(2048);
        using var publicOnly = RSA.Create();
        publicOnly.ImportParameters(source.ExportParameters(includePrivateParameters: false));

        var act = () => RsaKeyMaterial.FromKeyPair(publicOnly);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void FromModulusExponent_BuildsPublicMaterial()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        var material = RsaKeyMaterial.FromModulusExponent(parameters.Modulus!, parameters.Exponent!);

        material.HasPrivateKey.Should().BeFalse();
        material.Modulus.ToArray().Should().Equal(parameters.Modulus!);
        material.Exponent.ToArray().Should().Equal(parameters.Exponent!);
    }

    [Fact]
    public void FromKeyPair_UndersizedKey_Throws()
    {
        using var rsa = TestCertificates.CreateRsaKey(1024);

        var act = () => RsaKeyMaterial.FromKeyPair(rsa);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void ToPublicOnly_DropsPrivateComponents()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var pair = RsaKeyMaterial.FromKeyPair(rsa);

        var pub = pair.ToPublicOnly();

        pub.HasPrivateKey.Should().BeFalse();
        pub.Modulus.ToArray().Should().Equal(pair.Modulus.ToArray());
        pub.Exponent.ToArray().Should().Equal(pair.Exponent.ToArray());
    }

    [Fact]
    public void ToPublicOnly_AlreadyPublic_ReturnsSameInstance()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var pub = RsaKeyMaterial.FromPublicKey(rsa);

        pub.ToPublicOnly().Should().BeSameAs(pub);
    }

    [Fact]
    public void CreateRsa_RoundTripsPublicKey()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var material = RsaKeyMaterial.FromKeyPair(rsa);

        using var rebuilt = material.CreateRsa();

        rebuilt.ExportParameters(includePrivateParameters: false).Modulus
            .Should().Equal(rsa.ExportParameters(includePrivateParameters: false).Modulus);
    }

    [Fact]
    public void FromModulusExponent_StripsLeadingZeroToCanonicalForm()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        // Prepend a spurious 0x00 sign byte, as a DER INTEGER encoding may carry.
        var padded = new byte[parameters.Modulus!.Length + 1];
        Array.Copy(parameters.Modulus, 0, padded, 1, parameters.Modulus.Length);

        var material = RsaKeyMaterial.FromModulusExponent(padded, parameters.Exponent!);

        material.Modulus.ToArray().Should().Equal(parameters.Modulus); // canonical: no leading zero
        material.KeySizeBits.Should().Be(2048);
    }

    /// <summary>
    /// Issue #117: canonicalizing only the <em>exposed</em> bytes was not enough. The RSA parameters
    /// kept for <see cref="RsaKeyMaterial.CreateRsa"/> still carried the sign byte, so the imported key
    /// was 2056 bits and every padded operation on it failed — while <c>KeySizeBits</c> and the
    /// fingerprint claimed 2048. A real client (node-ebics-client) sends exactly this encoding, which
    /// is why its HPB could not be answered.
    /// </summary>
    [Fact]
    public void FromModulusExponent_WithLeadingZero_ImportsAKeyThatCanEncrypt()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var padded = new byte[parameters.Modulus!.Length + 1];
        Array.Copy(parameters.Modulus, 0, padded, 1, parameters.Modulus.Length);

        var material = RsaKeyMaterial.FromModulusExponent(padded, parameters.Exponent!);

        using var imported = material.CreateRsa();
        imported.KeySize.Should().Be(2048, "the imported key must match the canonical modulus length");

        // The operation that used to throw: OAEP-SHA256 (E002) against the reconstructed public key.
        var encrypted = imported.Encrypt(new byte[16], RSAEncryptionPadding.OaepSHA256);
        rsa.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256).Should().Equal(new byte[16]);
    }

    [Fact]
    public void Modulus_IsDefensivelyCopiedFromInput()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var modulus = (byte[])parameters.Modulus!.Clone();

        var material = RsaKeyMaterial.FromModulusExponent(modulus, parameters.Exponent!);
        var snapshot = material.Modulus.ToArray();

        modulus[0] ^= 0xFF; // mutate the caller's array after construction

        material.Modulus.ToArray().Should().Equal(snapshot);
    }
}
