using AwesomeAssertions;
using EBICO.Core.Crypto;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for the <see cref="KeyVersion"/> value object — shape validation, purpose derivation
/// and the deliberate split between "well-formed" and "known" (issue #18). Tier A —
/// self-contained, no proprietary samples.
/// </summary>
public class KeyVersionTests
{
    [Theory]
    [InlineData("A004", KeyPurpose.Signature)]
    [InlineData("A005", KeyPurpose.Signature)]
    [InlineData("A006", KeyPurpose.Signature)]
    [InlineData("E001", KeyPurpose.Encryption)]
    [InlineData("E002", KeyPurpose.Encryption)]
    [InlineData("X001", KeyPurpose.Authentication)]
    [InlineData("X002", KeyPurpose.Authentication)]
    public void Create_ValidCode_RoundTripsValueAndPurpose(string code, KeyPurpose expected)
    {
        var version = KeyVersion.Create(code);

        version.Value.Should().Be(code);
        version.Purpose.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A05")]
    [InlineData("A0050")]
    [InlineData("a005")]
    [InlineData("B005")]
    [InlineData("A05X")]
    [InlineData("AAAA")]
    [InlineData(" A005")]
    public void Create_MalformedCode_Throws(string code)
    {
        var act = () => KeyVersion.Create(code);

        act.Should().Throw<InvalidKeyVersionException>();
    }

    [Fact]
    public void Create_Null_Throws()
    {
        var act = () => KeyVersion.Create(null!);

        act.Should().Throw<InvalidKeyVersionException>();
    }

    [Fact]
    public void Create_WellFormedButUnknown_IsAcceptedButDoesNotResolve()
    {
        var version = KeyVersion.Create("A999");

        version.Value.Should().Be("A999");
        version.Purpose.Should().Be(KeyPurpose.Signature);
        KeyVersions.TryGet(version, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("A005", true)]
    [InlineData("E002", true)]
    [InlineData("bad", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryCreate_ReflectsValidity(string? code, bool expected)
    {
        KeyVersion.TryCreate(code, out var version).Should().Be(expected);

        if (expected)
        {
            version.Value.Should().Be(code);
        }
    }

    [Fact]
    public void ToString_ReturnsCode()
        => KeyVersion.Create("X002").ToString().Should().Be("X002");

    [Fact]
    public void Equality_SameCode_AreEqual()
        => KeyVersion.Create("A005").Should().Be(KeyVersion.Create("A005"));

    [Fact]
    public void Equality_DifferentCode_AreNotEqual()
        => KeyVersion.Create("A005").Should().NotBe(KeyVersion.Create("A006"));

    [Fact]
    public void Default_HasNullValue()
    {
        // Caveat of the struct value-object form: the default instance bypasses the factory.
        default(KeyVersion).Value.Should().BeNull();
    }
}
