using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="BankSignature"/> — the EBICS bank-technical signature (sign + verify)
/// for A005 (RSASSA-PKCS1-v1_5) and A006 (RSASSA-PSS) over SHA-256, plus the order hash
/// (issue #19). Tier A — keys are generated in-process via <see cref="TestCertificates"/>; no
/// proprietary samples. PKCS1-v1.5 (A005) is deterministic and pinned with a known-answer
/// vector; PSS (A006) is randomised, so it is covered by round-trip and cross-verify only.
/// </summary>
public class BankSignatureTests
{
    private static readonly KeyVersion A005 = KeyVersion.Create("A005");
    private static readonly KeyVersion A006 = KeyVersion.Create("A006");

    private static readonly byte[] OrderData = "EBICO order data for issue 19"u8.ToArray();

    // The same fixed 2048-bit key pinned in RsaKeyImportExportTests, reused here so the A005
    // known-answer vector below stays anchored to a single, stable key.
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

    // The order data the A005 known-answer vector was generated over.
    private static readonly byte[] KatOrderData = "EBICO order-data KAT #19"u8.ToArray();

    // The expected A005 (RSASSA-PKCS1-v1_5 over SHA-256) signature of KatOrderData with the
    // pinned key. PKCS1-v1.5 is deterministic, so this byte sequence must reproduce exactly.
    private const string ExpectedA005SignatureBase64 =
        "pa0H9CdolW2OjsQlLpIWU/0smgh94Zjxy5DOYaxxRcwmjSj5hlf6vEL9CemEcCieRkUCoJ8N4eB" +
        "gwAzOxi2zH/qEhw2Pu4z/gQsTVJeF3Yyn96cR/xB4UxzTunM8OLJAkurDdYZoCaKNRtDKDuf3MI" +
        "u3Iqny9oyqsIicgh7JAySFRCGWjF0oNSYECMfS+IxbBtZDZ2owiUvnbJLi3+8k53+LWAQfOlRSX" +
        "mxkw7E5hkN83l2AXbelNCGX1NmIL4lQ3g0vIzQVZB0B26+kt1OuJc/ia6GohAnOnzhHSIHdZT08" +
        "HOeQ91XkqgTV23crFKgAtA0nvbGHtI5cVUYgD1lyNw==";

    private static RsaKeyMaterial NewKeyPair()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        return RsaKeyMaterial.FromKeyPair(rsa);
    }

    [Fact]
    public void Sign_Then_Verify_A005_Succeeds()
    {
        var key = NewKeyPair();

        var signature = BankSignature.Sign(OrderData, key, A005);

        BankSignature.Verify(OrderData, signature, key, A005).Should().BeTrue();
    }

    [Fact]
    public void Sign_Then_Verify_A006_Succeeds()
    {
        var key = NewKeyPair();

        var signature = BankSignature.Sign(OrderData, key, A006);

        BankSignature.Verify(OrderData, signature, key, A006).Should().BeTrue();
    }

    [Fact]
    public void ComputeOrderHash_IsSha256_AndDrivesSignHashRoundTrip()
    {
        var key = NewKeyPair();

        var hash = BankSignature.ComputeOrderHash(OrderData);
        hash.Length.Should().Be(32);

        var signature = BankSignature.SignHash(hash, key, A005);
        BankSignature.VerifyHash(hash, signature, key, A005).Should().BeTrue();
    }

    [Theory]
    [InlineData("A005")]
    [InlineData("A006")]
    public void Verify_WithPublicOnlyKey_Succeeds(string code)
    {
        var version = KeyVersion.Create(code);
        var key = NewKeyPair();

        var signature = BankSignature.Sign(OrderData, key, version);

        BankSignature.Verify(OrderData, signature, key.ToPublicOnly(), version).Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedData_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = BankSignature.Sign(OrderData, key, A005);

        var tampered = (byte[])OrderData.Clone();
        tampered[0] ^= 0xFF;

        BankSignature.Verify(tampered, signature, key, A005).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedSignature_ReturnsFalse()
    {
        var key = NewKeyPair();
        var signature = BankSignature.Sign(OrderData, key, A005);
        signature[0] ^= 0xFF;

        BankSignature.Verify(OrderData, signature, key, A005).Should().BeFalse();
    }

    [Fact]
    public void Verify_ShortSignature_ReturnsFalse()
    {
        var key = NewKeyPair();

        BankSignature.Verify(OrderData, new byte[] { 1, 2, 3 }, key, A005).Should().BeFalse();
    }

    [Theory]
    [InlineData("A005")]
    [InlineData("A006")]
    public void Verify_WrongKey_ReturnsFalse(string code)
    {
        var version = KeyVersion.Create(code);
        var signer = NewKeyPair();
        var other = NewKeyPair();

        var signature = BankSignature.Sign(OrderData, signer, version);

        BankSignature.Verify(OrderData, signature, other, version).Should().BeFalse();
    }

    [Theory]
    [InlineData("E002")]
    [InlineData("X002")]
    [InlineData("A999")]
    public void Sign_NonSignatureOrUnknownVersion_Throws(string code)
    {
        var key = NewKeyPair();

        var act = () => BankSignature.Sign(OrderData, key, KeyVersion.Create(code));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sign_DefaultVersion_Throws()
    {
        var key = NewKeyPair();

        var act = () => BankSignature.Sign(OrderData, key, default);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Verify_NonSignatureVersion_Throws()
    {
        var key = NewKeyPair();
        var signature = BankSignature.Sign(OrderData, key, A005);

        var act = () => BankSignature.Verify(OrderData, signature, key, KeyVersion.Create("X002"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sign_PublicOnlyKey_Throws()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var publicOnly = RsaKeyMaterial.FromPublicKey(rsa);

        var act = () => BankSignature.Sign(OrderData, publicOnly, A005);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void Sign_NullKey_Throws()
    {
        var act = () => BankSignature.Sign(OrderData, null!, A005);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void KnownVector_A005_ProducesPinnedSignature()
    {
        var key = RsaKeyImportExport.ImportPkcs8(Convert.FromBase64String(KnownPkcs8Base64));

        var signature = BankSignature.Sign(KatOrderData, key, A005);

        signature.Should().Equal(Convert.FromBase64String(ExpectedA005SignatureBase64));
        BankSignature.Verify(KatOrderData, signature, key.ToPublicOnly(), A005).Should().BeTrue();
    }

    [Fact]
    public void A006_Pss_IsRandomised_YetBothSignaturesVerify()
    {
        var key = NewKeyPair();

        var first = BankSignature.Sign(OrderData, key, A006);
        var second = BankSignature.Sign(OrderData, key, A006);

        first.Should().NotEqual(second);
        BankSignature.Verify(OrderData, first, key, A006).Should().BeTrue();
        BankSignature.Verify(OrderData, second, key, A006).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithDifferentVersionThanSigned_ReturnsFalse()
    {
        var key = NewKeyPair();

        var a005Signature = BankSignature.Sign(OrderData, key, A005);
        var a006Signature = BankSignature.Sign(OrderData, key, A006);

        // A005 (PKCS1-v1.5) and A006 (PSS) are different padding schemes over the same key.
        BankSignature.Verify(OrderData, a005Signature, key, A006).Should().BeFalse();
        BankSignature.Verify(OrderData, a006Signature, key, A005).Should().BeFalse();
    }
}
