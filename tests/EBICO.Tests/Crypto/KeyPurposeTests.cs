using AwesomeAssertions;
using EBICO.Core.Crypto;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="KeyPurpose"/> and its letter mapping (A/E/X) — issue #18. Tier A —
/// self-contained, no proprietary samples.
/// </summary>
public class KeyPurposeTests
{
    [Theory]
    [InlineData(KeyPurpose.Signature, 'A')]
    [InlineData(KeyPurpose.Encryption, 'E')]
    [InlineData(KeyPurpose.Authentication, 'X')]
    public void Letter_ReturnsVersionLetter(KeyPurpose purpose, char expected)
        => purpose.Letter().Should().Be(expected);

    [Theory]
    [InlineData('A', KeyPurpose.Signature)]
    [InlineData('E', KeyPurpose.Encryption)]
    [InlineData('X', KeyPurpose.Authentication)]
    public void TryFromLetter_KnownLetter_ResolvesPurpose(char letter, KeyPurpose expected)
    {
        KeyPurposeExtensions.TryFromLetter(letter, out var purpose).Should().BeTrue();
        purpose.Should().Be(expected);
    }

    [Theory]
    [InlineData('a')]
    [InlineData('B')]
    [InlineData('Z')]
    [InlineData('1')]
    public void TryFromLetter_UnknownLetter_ReturnsFalse(char letter)
        => KeyPurposeExtensions.TryFromLetter(letter, out _).Should().BeFalse();

    [Fact]
    public void SignatureLetter_IsTheVersionLetterA()
    {
        // KeyPurpose.Signature maps to the version letter 'A' (A00x). This is unrelated to
        // SignatureClass.A (the "Erstunterschrift" authorisation level) — same letter, different concept.
        KeyPurpose.Signature.Letter().Should().Be('A');
    }
}
