using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Suite.Services;

namespace EBICO.Tests.Suite;

/// <summary>
/// Tests for the INI-letter fingerprint parsing helper <see cref="FingerprintFormat"/> (issue #55):
/// free-form hex (whitespace, case) parses to the raw digest; invalid input is rejected.
/// </summary>
public class FingerprintFormatTests
{
    // --- Happy path ---

    [Theory]
    [InlineData("1a2b3c")]
    [InlineData("1A 2B 3C")]
    [InlineData("1A2B\n3C")]
    [InlineData("  1a 2b\t3c  ")]
    public void TryParseHex_AcceptsWhitespaceAndCase(string input)
    {
        FingerprintFormat.TryParseHex(input, out var digest).Should().BeTrue();
        digest.Should().Equal((byte)0x1a, 0x2b, 0x3c);
    }

    [Fact]
    public void TryParseHex_RoundTripsLetterFormat()
    {
        var bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var letter = PublicKeyFingerprint.ToLetterFormat(bytes);

        FingerprintFormat.TryParseHex(letter, out var parsed).Should().BeTrue();
        parsed.Should().Equal(bytes);
    }

    [Fact]
    public void Normalize_RemovesAllWhitespace()
        => FingerprintFormat.Normalize(" 1a\t2b\n3c ").Should().Be("1a2b3c");

    // --- Negative cases ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1a2")]  // odd length
    [InlineData("zzzz")] // not hex
    public void TryParseHex_RejectsInvalid(string? input)
    {
        FingerprintFormat.TryParseHex(input, out var digest).Should().BeFalse();
        digest.Should().BeNull();
    }
}
