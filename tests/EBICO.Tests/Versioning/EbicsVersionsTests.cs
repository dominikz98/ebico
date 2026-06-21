using AwesomeAssertions;
using EBICO.Core;
using EBICO.Core.Versioning;
using H003 = EBICO.Core.Schema.H003;
using H004 = EBICO.Core.Schema.H004;
using H005 = EBICO.Core.Schema.H005;

namespace EBICO.Tests.Versioning;

/// <summary>
/// Tests for the <see cref="EbicsVersions"/> registry: the single source of truth that
/// maps an <see cref="EbicsVersion"/> to its metadata and offers reverse lookups by
/// namespace and by code (issue #14).
/// </summary>
public class EbicsVersionsTests
{
    [Fact]
    public void All_ListsTheThreeVersions_OrderedOldestToNewest()
    {
        EbicsVersions.All.Select(i => i.Version)
            .Should().Equal(EbicsVersion.H003, EbicsVersion.H004, EbicsVersion.H005);
    }

    [Theory]
    [InlineData(EbicsVersion.H003, "H003", "http://www.ebics.org/H003")]
    [InlineData(EbicsVersion.H004, "H004", "urn:org:ebics:H004")]
    [InlineData(EbicsVersion.H005, "H005", "urn:org:ebics:H005")]
    public void Get_ReturnsCodeAndNamespace(EbicsVersion version, string code, string namespaceUri)
    {
        var info = EbicsVersions.Get(version);

        info.Version.Should().Be(version);
        info.Code.Should().Be(code);
        info.NamespaceUri.Should().Be(namespaceUri);
    }

    [Theory]
    [MemberData(nameof(ExpectedEnvelopeTypes))]
    public void Get_WiresExpectedClrEnvelopeTypes(
        EbicsVersion version,
        Type request,
        Type response,
        Type unsecured,
        Type unsigned,
        Type noPubKey,
        Type keyManagementResponse)
    {
        var info = EbicsVersions.Get(version);

        info.RequestType.Should().Be(request);
        info.ResponseType.Should().Be(response);
        info.UnsecuredRequestType.Should().Be(unsecured);
        info.UnsignedRequestType.Should().Be(unsigned);
        info.NoPubKeyDigestsRequestType.Should().Be(noPubKey);
        info.KeyManagementResponseType.Should().Be(keyManagementResponse);
    }

    public static TheoryData<EbicsVersion, Type, Type, Type, Type, Type, Type> ExpectedEnvelopeTypes() => new()
    {
        {
            EbicsVersion.H003,
            typeof(H003.EbicsRequest), typeof(H003.EbicsResponse),
            typeof(H003.EbicsUnsecuredRequest), typeof(H003.EbicsUnsignedRequest),
            typeof(H003.EbicsNoPubKeyDigestsRequest), typeof(H003.EbicsKeyManagementResponse)
        },
        {
            EbicsVersion.H004,
            typeof(H004.EbicsRequest), typeof(H004.EbicsResponse),
            typeof(H004.EbicsUnsecuredRequest), typeof(H004.EbicsUnsignedRequest),
            typeof(H004.EbicsNoPubKeyDigestsRequest), typeof(H004.EbicsKeyManagementResponse)
        },
        {
            EbicsVersion.H005,
            typeof(H005.EbicsRequest), typeof(H005.EbicsResponse),
            typeof(H005.EbicsUnsecuredRequest), typeof(H005.EbicsUnsignedRequest),
            typeof(H005.EbicsNoPubKeyDigestsRequest), typeof(H005.EbicsKeyManagementResponse)
        },
    };

    [Fact]
    public void Get_UndefinedVersionValue_ThrowsArgumentOutOfRange()
    {
        var act = () => EbicsVersions.Get((EbicsVersion)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("http://www.ebics.org/H003", EbicsVersion.H003)]
    [InlineData("urn:org:ebics:H004", EbicsVersion.H004)]
    [InlineData("urn:org:ebics:H005", EbicsVersion.H005)]
    public void TryFromNamespace_KnownNamespace_ReturnsTrueWithInfo(string namespaceUri, EbicsVersion expected)
    {
        EbicsVersions.TryFromNamespace(namespaceUri, out var info).Should().BeTrue();

        info.Should().NotBeNull();
        info!.Version.Should().Be(expected);
    }

    [Fact]
    public void TryFromNamespace_UnknownNamespace_ReturnsFalse()
    {
        EbicsVersions.TryFromNamespace("urn:org:ebics:H999", out var info).Should().BeFalse();

        info.Should().BeNull();
    }

    [Fact]
    public void TryFromNamespace_Null_ReturnsFalse()
    {
        EbicsVersions.TryFromNamespace(null, out var info).Should().BeFalse();

        info.Should().BeNull();
    }

    [Fact]
    public void TryFromNamespace_IsCaseSensitive()
    {
        // Ordinal comparison: a different-cased namespace must not resolve.
        EbicsVersions.TryFromNamespace("URN:ORG:EBICS:H005", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("H003", EbicsVersion.H003)]
    [InlineData("H004", EbicsVersion.H004)]
    [InlineData("H005", EbicsVersion.H005)]
    public void TryFromCode_KnownCode_ReturnsTrueWithInfo(string code, EbicsVersion expected)
    {
        EbicsVersions.TryFromCode(code, out var info).Should().BeTrue();

        info.Should().NotBeNull();
        info!.Version.Should().Be(expected);
    }

    [Fact]
    public void TryFromCode_UnknownCode_ReturnsFalse()
    {
        EbicsVersions.TryFromCode("H002", out var info).Should().BeFalse();

        info.Should().BeNull();
    }

    [Fact]
    public void TryFromCode_Null_ReturnsFalse()
    {
        EbicsVersions.TryFromCode(null, out var info).Should().BeFalse();

        info.Should().BeNull();
    }

    [Fact]
    public void TryFromCode_IsCaseSensitive()
    {
        EbicsVersions.TryFromCode("h005", out _).Should().BeFalse();
    }
}
