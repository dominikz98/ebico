using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Crypto;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for the <see cref="KeyVersions"/> registry — known-version lookup, metadata, and the
/// per-EBICS-version permission rule (issue #18). Tier A — self-contained, no proprietary samples.
/// </summary>
public class KeyVersionsTests
{
    [Fact]
    public void All_ContainsTheSevenKnownVersionsInOrder()
    {
        KeyVersions.All.Select(i => i.Code)
            .Should().Equal("A004", "A005", "A006", "E001", "E002", "X001", "X002");
    }

    [Theory]
    [InlineData("A004")]
    [InlineData("A005")]
    [InlineData("A006")]
    [InlineData("E001")]
    [InlineData("E002")]
    [InlineData("X001")]
    [InlineData("X002")]
    public void Get_KnownVersion_ReturnsMatchingInfo(string code)
        => KeyVersions.Get(KeyVersion.Create(code)).Code.Should().Be(code);

    [Fact]
    public void Get_WellFormedButUnknown_Throws()
    {
        var act = () => KeyVersions.Get(KeyVersion.Create("A999"));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Get_Default_Throws()
    {
        var act = () => KeyVersions.Get(default);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("A004", EbicsVersion.H003, true)]
    [InlineData("A004", EbicsVersion.H004, true)]
    [InlineData("A004", EbicsVersion.H005, false)]
    [InlineData("A005", EbicsVersion.H003, true)]
    [InlineData("A005", EbicsVersion.H005, true)]
    // A006/PSS: EBICS 2.5 (H004) onwards — see the Spec-Vorbehalt on KeyVersions (issue #117).
    [InlineData("A006", EbicsVersion.H003, false)]
    [InlineData("A006", EbicsVersion.H004, true)]
    [InlineData("A006", EbicsVersion.H005, true)]
    [InlineData("E001", EbicsVersion.H005, false)]
    [InlineData("E002", EbicsVersion.H003, true)]
    [InlineData("E002", EbicsVersion.H005, true)]
    [InlineData("X001", EbicsVersion.H005, false)]
    [InlineData("X002", EbicsVersion.H005, true)]
    public void IsPermitted_FollowsPermissionTable(string code, EbicsVersion version, bool expected)
        => KeyVersions.IsPermitted(KeyVersion.Create(code), version).Should().Be(expected);

    [Fact]
    public void IsPermitted_UnknownVersion_ReturnsFalse()
        => KeyVersions.IsPermitted(KeyVersion.Create("A999"), EbicsVersion.H005).Should().BeFalse();

    [Fact]
    public void EnsurePermitted_Permitted_ReturnsInfo()
        => KeyVersions.EnsurePermitted(KeyVersion.Create("A006"), EbicsVersion.H005).Code.Should().Be("A006");

    [Fact]
    public void EnsurePermitted_NotPermitted_Throws()
    {
        var act = () => KeyVersions.EnsurePermitted(KeyVersion.Create("A006"), EbicsVersion.H003);

        act.Should().Throw<KeyVersionNotPermittedException>();
    }

    [Fact]
    public void EnsurePermitted_UnknownVersion_ThrowsArgumentOutOfRange()
    {
        var act = () => KeyVersions.EnsurePermitted(KeyVersion.Create("A999"), EbicsVersion.H005);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(KeyPurpose.Signature, 3)]
    [InlineData(KeyPurpose.Encryption, 2)]
    [InlineData(KeyPurpose.Authentication, 2)]
    public void ForPurpose_ReturnsAllKnownVersionsForThatPurpose(KeyPurpose purpose, int count)
        => KeyVersions.ForPurpose(purpose).Should().HaveCount(count).And.OnlyContain(i => i.Purpose == purpose);

    [Fact]
    public void PermittedFor_SignatureH005_ExcludesLegacyIncludesA005A006()
    {
        KeyVersions.PermittedFor(KeyPurpose.Signature, EbicsVersion.H005)
            .Select(i => i.Code).Should().Equal("A005", "A006");
    }

    [Fact]
    public void PermittedFor_SignatureH004_IncludesLegacyAndA006()
    {
        KeyVersions.PermittedFor(KeyPurpose.Signature, EbicsVersion.H004)
            .Select(i => i.Code).Should().Equal("A004", "A005", "A006");
    }

    [Fact]
    public void PermittedFor_SignatureH003_ExcludesA006()
    {
        KeyVersions.PermittedFor(KeyPurpose.Signature, EbicsVersion.H003)
            .Select(i => i.Code).Should().Equal("A004", "A005");
    }

    [Theory]
    [InlineData(KeyPurpose.Signature, "A005")]
    [InlineData(KeyPurpose.Encryption, "E002")]
    [InlineData(KeyPurpose.Authentication, "X002")]
    public void Default_ReturnsCurrentVersion(KeyPurpose purpose, string expected)
        => KeyVersions.Default(purpose, EbicsVersion.H005).Code.Should().Be(expected);

    [Fact]
    public void Metadata_FlagsLegacyAndPaddingIntent()
    {
        var a004 = KeyVersions.Get(KeyVersion.Create("A004"));
        a004.IsLegacy.Should().BeTrue();
        a004.PaddingIntent.Should().Be(RsaPaddingScheme.Pkcs1V15);

        var a006 = KeyVersions.Get(KeyVersion.Create("A006"));
        a006.IsLegacy.Should().BeFalse();
        a006.PaddingIntent.Should().Be(RsaPaddingScheme.Pss);

        KeyVersions.Get(KeyVersion.Create("E002")).PaddingIntent.Should().Be(RsaPaddingScheme.Oaep);
        KeyVersions.Get(KeyVersion.Create("E001")).PaddingIntent.Should().Be(RsaPaddingScheme.Pkcs1V15Encryption);
    }
}
