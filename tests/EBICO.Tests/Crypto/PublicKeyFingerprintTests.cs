using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="PublicKeyFingerprint"/> — the EBICS public-key fingerprint (SHA-256 over
/// the exponent/modulus hash input) used in the INI letter, HPB response and BankPubKeyDigests
/// (issue #22). Tier A — keys are generated in-process via <see cref="TestCertificates"/>; no
/// proprietary samples. The fingerprint is deterministic, so it is pinned with known-answer
/// vectors anchored to the same fixed key used by <see cref="BankSignatureTests"/>.
/// </summary>
public class PublicKeyFingerprintTests
{
    // The same fixed 2048-bit key pinned in BankSignatureTests/EncryptionE002Tests, reused here so
    // the known-answer vectors below stay anchored to a single, stable key. Its public exponent is
    // 65537 (0x010001), which exercises the leading-zero-nibble strip in the hash input.
    private const string KnownPkcs8Base64 =
        "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCqB3mNXiQjOc9m" +
        "vpfoUYmMbD6I9ImTgtJ9PNkmf35GbCgoBOXaPZrt16/kphXguYCsTafUz/HSNxn" +
        "nTIzM6YVzf4mS9FiJ1gXO5CEHB8H0GRc2m54EFoPdVWX9sgG0vKmkGeMM8zKZAV" +
        "x864g9JgmWzoZ6AYBgMTWhHMxCJTlmgd4ooIH8Ic6Nrp+h1/hngVLYS6EXN6chn" +
        "piTyHxJIYCiV5prnXNxr8Gbq9dzR+D5WnqVv6Mj+aEkOFv9t+gLQm309hXF+2Ln" +
        "uz0rrNoZc6TkKy90VS/kf7aczxilKmyJkaa1kd45azybd6VFaV4fHintRYgrLMw" +
        "O1Ng6mOMOe6OVAgMBAAECggEBAI+yL4VNKadnpSPFMibSOjgmBxfB6z9ykafsM+14" +
        "VGT55VolAYjXBce6wFmyD81TmS6FlrChaVLq5IZ5SImpVfKNt9wti2I6McBvRoZ" +
        "lhQJh7h9llz8HNDxrfv3QYea4h3o7sorwQjPTVbHxcLuVGQeN1VLpT/B7xkI6T1o" +
        "bkY5SFCLcQH5vBWRl6ucXGU4Inn06unho5B9SwifBHyy8CPHjPGpWlgVA/VT4og" +
        "X6M3MmA2c+TiHK0wfwXRo8+3q2WfsQjo1qZ5Uk9Lf01CzcCz+f2QbvaPa3Lg0iN" +
        "oczD51QZIPxUQCVOKd2Kh4XNqp95mPHlC67f7Db/6M5xkYVo2UCgYEAxb2oNmbk" +
        "dNODrdVWGz51/az0Rk988tCGmWVazJUmrtM2Xdqa3ZgHNbhS+jpHtBNZaIHcl9w" +
        "50IkTqkB4QaL4+nIFJE8vR9k8+AX4EkhfcRIN2AZq+WVkfKpAVPvD3IgdkfYYUiw" +
        "iv8W/UzrUW+hK7gNoFG+s2sxx5OvmzPwOuZsCgYEA3B+z/UYC3A6dhLpy85E5myEC" +
        "j3O+195ObGzVb+3VKSprJ7t6LIY4uR6PXfiOBfctb1NooMd3GO67FeLvq/3/6WA" +
        "zFzxAFN188bhddL8csnv1rBxGKfOmZrN6zahBmw4hsbvBPVIgjWDnYRW4OGijgMF" +
        "j3+LmPV0ooc5lR524Qo8CgYBWFn/JT3peskc9wwc9zS+pRUcD5U9MlyRCXDHvp2+z" +
        "5RhiO+34U1uwM5NMhVr6NwJR0VesdaBl/YemM3MngEBNKJ68dAzthtJYWKDrtL5" +
        "4h5enWQPxmAbrj2N6nDFlLY1SIoXsIHLwcrMdFRum97bHcIw7eXMTvrZHJ7zPuVz" +
        "fyQKBgBnQxgUgHtm8BRE55J1YHM9qsagtROaANeZVZTq5Q9SOGv8P56YtH53mTZ4" +
        "RtmZQtM1nlM+2VOthpCNO+BjNsyOlmphRAprv1uVqX9t/RlhQXWGP91KYNp240uA" +
        "nqXoL0DvN7z3H0fWCteAW8gH7k6FYDOSG8cWklU1UrWAWyTNVAoGAFNPqjOPy0Q1D" +
        "hF6BwjGVqJcDznwagHA6oU7e0+7TprUAek4WHmqQNvQZCXu223UUzTOBvuw/Waso" +
        "c91V5bJslNTgm05QXlBUYeR6PCGUN9XAhfbZArdcNOxLDTrf4rK5O4vbKT1yycLB" +
        "kjCZgKKmLS1H+slZ4UxClx0t13eowmc=";

    // KAT vectors, pinned from the fixed key above. The hash input is "<exponent-hex> <modulus-hex>"
    // (exponent 65537 -> "10001", lowercase, single space); the fingerprint is its SHA-256; the
    // letter form is the uppercase byte-pair grouping of that digest.
    private const string ExpectedHashInput =
        "10001 aa07798d5e242339cf66be97e851898c6c3e88f4899382d27d3cd9267f7e466c282804e5da3d9aed" +
        "d7afe4a615e0b980ac4da7d4cff1d23719e74c8ccce985737f8992f45889d605cee4210707c1f41917369b" +
        "9e041683dd5565fdb201b4bca9a419e30cf33299015c7ceb883d260996ce867a0180603135a11ccc422539" +
        "6681de28a081fc21ce8dae9fa1d7f8678152d84ba11737a7219e9893c87c492180a2579a6b9d7371afc19b" +
        "abd77347e0f95a7a95bfa323f9a124385bfdb7e80b426df4f615c5fb62e7bb3d2bacda1973a4e42b2f7455" +
        "2fe47fb69ccf18a52a6c8991a6b591de396b3c9b77a545695e1f1e29ed45882b2ccc0ed4d83a98e30e7ba395";

    private const string ExpectedFingerprintBase64 = "cxbKyzStzX2oKxcyq/UL0GerfBRAP4gooQaNvgQtd/E=";

    private const string ExpectedLetterFormat =
        "73 16 CA CB 34 AD CD 7D\n" +
        "A8 2B 17 32 AB F5 0B D0\n" +
        "67 AB 7C 14 40 3F 88 28\n" +
        "A1 06 8D BE 04 2D 77 F1";

    private static RsaKeyMaterial PinnedKey()
        => RsaKeyImportExport.ImportPkcs8(Convert.FromBase64String(KnownPkcs8Base64));

    private static RsaKeyMaterial NewKeyPair()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        return RsaKeyMaterial.FromKeyPair(rsa);
    }

    // --- Known-answer vectors (core of the DoD) --------------------------------

    [Fact]
    public void Compute_KnownKey_ProducesPinnedDigest()
    {
        var digest = PublicKeyFingerprint.Compute(PinnedKey());

        digest.Should().Equal(Convert.FromBase64String(ExpectedFingerprintBase64));
    }

    [Fact]
    public void BuildHashInput_KnownKey_ProducesPinnedString()
    {
        PublicKeyFingerprint.BuildHashInput(PinnedKey()).Should().Be(ExpectedHashInput);
    }

    [Fact]
    public void BuildHashInput_Exponent65537_StripsLeadingZeroNibble()
    {
        // 65537 == 0x010001; canonical bytes are {01,00,01} (no leading zero byte), but the hex
        // "010001" carries a leading zero nibble that must be stripped to "10001".
        var input = PublicKeyFingerprint.BuildHashInput(PinnedKey());

        input.Should().StartWith("10001 ");
        input.Should().NotStartWith("010001");
    }

    [Fact]
    public void ToLetterFormat_KnownDigest_ProducesGroupedHex()
    {
        var digest = PublicKeyFingerprint.Compute(PinnedKey());

        PublicKeyFingerprint.ToLetterFormat(digest).Should().Be(ExpectedLetterFormat);
    }

    // --- Happy path / self-consistency ----------------------------------------

    [Fact]
    public void Compute_ReturnsThirtyTwoBytes()
    {
        PublicKeyFingerprint.Compute(NewKeyPair()).Should().HaveCount(32);
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        var key = NewKeyPair();

        PublicKeyFingerprint.Compute(key).Should().Equal(PublicKeyFingerprint.Compute(key));
    }

    [Fact]
    public void Compute_PublicOnlyAndKeyPair_Match()
    {
        var key = NewKeyPair();

        PublicKeyFingerprint.Compute(key.ToPublicOnly())
            .Should().Equal(PublicKeyFingerprint.Compute(key));
    }

    [Fact]
    public void Compute_FromCertificatePublicKey_MatchesKeyMaterial()
    {
        // The H005 path (certificate) and the H003/H004 path (raw RSA key) must yield the same
        // fingerprint for the same underlying key.
        using var cert = TestCertificates.CreateSelfSigned("CN=EBICO fingerprint test");
        using var rsaFromCert = cert.GetRSAPublicKey()!;

        var viaCert = PublicKeyFingerprint.Compute(RsaKeyImportExport.ImportPublicKeyFromCertificate(cert));
        var viaKey = PublicKeyFingerprint.Compute(RsaKeyMaterial.FromPublicKey(rsaFromCert));

        viaCert.Should().Equal(viaKey);
    }

    // --- Verify / negative cases ----------------------------------------------

    [Fact]
    public void Verify_MatchingDigest_ReturnsTrue()
    {
        var key = NewKeyPair();

        PublicKeyFingerprint.Verify(key, PublicKeyFingerprint.Compute(key)).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongDigest_ReturnsFalse()
    {
        var key = NewKeyPair();
        var tampered = PublicKeyFingerprint.Compute(key);
        tampered[0] ^= 0xFF;

        PublicKeyFingerprint.Verify(key, tampered).Should().BeFalse();
    }

    [Fact]
    public void Verify_TruncatedDigest_ReturnsFalse()
    {
        var key = NewKeyPair();

        PublicKeyFingerprint.Verify(key, new byte[] { 1, 2, 3 }).Should().BeFalse();
    }

    [Fact]
    public void Verify_DifferentKey_ReturnsFalse()
    {
        var key = NewKeyPair();
        var other = NewKeyPair();

        PublicKeyFingerprint.Verify(other, PublicKeyFingerprint.Compute(key)).Should().BeFalse();
    }

    [Fact]
    public void Verify_NullKey_Throws()
    {
        var act = () => PublicKeyFingerprint.Verify(null!, new byte[32]);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compute_NullKey_Throws()
    {
        var act = () => PublicKeyFingerprint.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHashInput_NullKey_Throws()
    {
        var act = () => PublicKeyFingerprint.BuildHashInput(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
