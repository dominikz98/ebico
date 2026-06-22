using AwesomeAssertions;
using EBICO.Core.Domain;

namespace EBICO.Tests.Domain;

/// <summary>
/// Tests for the EBICS identifier value objects (<see cref="HostId"/>, <see cref="PartnerId"/>,
/// <see cref="UserId"/>, <see cref="SystemId"/>) and their shared validation against the
/// schema pattern <c>[a-zA-Z0-9,=]{1,35}</c> (issue #16). Tier A — self-contained, no
/// proprietary samples.
/// </summary>
public class IdentifierTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("ABC123")]
    [InlineData("BANKDE01")]
    [InlineData("a,b=c")]
    public void Create_ValidValue_RoundTripsValue(string value)
    {
        HostId.Create(value).Value.Should().Be(value);
    }

    [Fact]
    public void Create_BoundaryLengths_OneAndThirtyFive_Accepted()
    {
        HostId.Create(new string('X', 1)).Value.Length.Should().Be(1);
        HostId.Create(new string('X', 35)).Value.Length.Should().Be(35);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AB CD")]
    [InlineData("AB/CD")]
    [InlineData("AB-CD")]
    [InlineData("ÄÖÜ")]
    public void Create_InvalidValue_Throws(string value)
    {
        var act = () => HostId.Create(value);

        act.Should().Throw<InvalidEbicsIdentifierException>();
    }

    [Fact]
    public void Create_TooLong_ThirtySix_Throws()
    {
        var act = () => HostId.Create(new string('X', 36));

        act.Should().Throw<InvalidEbicsIdentifierException>();
    }

    [Fact]
    public void Create_Null_Throws()
    {
        var act = () => HostId.Create(null!);

        act.Should().Throw<InvalidEbicsIdentifierException>();
    }

    [Theory]
    [InlineData("ABC123", true)]
    [InlineData("", false)]
    [InlineData("AB CD", false)]
    [InlineData(null, false)]
    public void TryCreate_ReflectsValidity(string? value, bool expected)
    {
        HostId.TryCreate(value, out var id).Should().Be(expected);

        if (expected)
        {
            id.Value.Should().Be(value);
        }
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        HostId.Create("BANKDE01").Should().Be(HostId.Create("BANKDE01"));
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        HostId.Create("BANKDE01").Should().NotBe(HostId.Create("BANKDE02"));
    }

    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        HostId.Create("BANKDE01").ToString().Should().Be("BANKDE01");
    }

    [Fact]
    public void Default_HasNullValue()
    {
        // Caveat of the struct value-object form: the default instance bypasses the factory.
        default(HostId).Value.Should().BeNull();
    }

    [Fact]
    public void AllIdentifierTypes_ShareValidation()
    {
        PartnerId.Create("PARTNER01").Value.Should().Be("PARTNER01");
        UserId.Create("USER0001").Value.Should().Be("USER0001");
        SystemId.Create("SYS00001").Value.Should().Be("SYS00001");

        var partner = () => PartnerId.Create("bad/value");
        var user = () => UserId.Create("bad value");
        var system = () => SystemId.Create("");

        partner.Should().Throw<InvalidEbicsIdentifierException>();
        user.Should().Throw<InvalidEbicsIdentifierException>();
        system.Should().Throw<InvalidEbicsIdentifierException>();
    }
}
