using AwesomeAssertions;
using EBICO.Connector.Configuration;
using EBICO.Core;

namespace EBICO.Tests.Connector;

/// <summary>Tests for <see cref="EbicsConnectionOptionsValidator"/> — happy path and negative cases.</summary>
public class EbicsConnectionOptionsValidatorTests
{
    private readonly EbicsConnectionOptionsValidator _validator = new();

    private static EbicsConnectionOptions Valid() => new()
    {
        Url = "https://bank.example/ebics",
        HostId = "EBIXHOST",
        PartnerId = "PARTNER01",
        UserId = "USER0001",
        Version = EbicsVersion.H005,
    };

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var result = _validator.Validate(name: null, Valid());

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingUrl_Fails()
    {
        var options = Valid();
        options.Url = null;

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(EbicsConnectionOptions.Url));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://bank.example/ebics")]
    [InlineData("/relative/path")]
    public void Validate_NonHttpUrl_Fails(string url)
    {
        var options = Valid();
        options.Url = url;

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(EbicsConnectionOptions.Url));
    }

    [Fact]
    public void Validate_InvalidHostId_Fails()
    {
        var options = Valid();
        options.HostId = "invalid host!";

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(EbicsConnectionOptions.HostId));
    }

    [Fact]
    public void Validate_MissingUserId_Fails()
    {
        var options = Valid();
        options.UserId = null;

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(EbicsConnectionOptions.UserId));
    }

    [Fact]
    public void Validate_UndefinedVersion_Fails()
    {
        var options = Valid();
        options.Version = (EbicsVersion)999;

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(EbicsConnectionOptions.Version));
    }

    [Fact]
    public void Validate_MultipleProblems_AreAllReported()
    {
        var options = new EbicsConnectionOptions
        {
            Url = null,
            HostId = null,
            PartnerId = null,
            UserId = null,
            Version = (EbicsVersion)999,
        };

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCountGreaterThanOrEqualTo(5);
    }
}
