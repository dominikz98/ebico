using System.Security.Cryptography;
using AwesomeAssertions;
using EBICO.Core.Crypto;
using EBICO.Tests.Infrastructure;

namespace EBICO.Tests.Crypto;

/// <summary>
/// Tests for <see cref="EncryptionE002"/> — the EBICS E002 hybrid transport encryption: AES-128-CBC
/// over the order data plus RSAES-OAEP-SHA256 over the transaction key (issue #21). Tier A — keys
/// are generated in-process via <see cref="TestCertificates"/>; no proprietary samples. The AES leg
/// (fixed key, zero IV, PKCS7) is deterministic and pinned with a known-answer vector; RSA-OAEP is
/// randomised, so it is covered by round-trip and cross-verify only.
/// </summary>
public class EncryptionE002Tests
{
    private static readonly KeyVersion E002 = KeyVersion.Create("E002");

    private static readonly byte[] OrderData = "EBICO order data for issue 21"u8.ToArray();

    // Deterministic AES-128-CBC known-answer vector: fixed 16-byte key + fixed plaintext, zero IV,
    // PKCS7 padding. Pins the IV and padding choices in EncryptionE002. Regenerate only if those
    // change (then update ExpectedAesCiphertextBase64 with the new bytes).
    private static readonly byte[] KatTransactionKey = "EBICO-E002-KAT!!"u8.ToArray();
    private static readonly byte[] KatPlaintext = "EBICO order data for E002 KAT #21"u8.ToArray();
    private const string ExpectedAesCiphertextBase64 =
        "nU6yw9na8IYWECarRFQRrOeq1PwnKAOIY/U5plhvdl3RBHqjgerUzqhOK0cfmsd4";

    // The same fixed 2048-bit key pinned in RsaKeyImportExportTests / BankSignatureTests, reused
    // here so the OAEP round-trip stays anchored to a single, stable key. (RSA-OAEP is randomised,
    // so its ciphertext itself cannot be byte-pinned.)
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

    private static RsaKeyMaterial NewKeyPair()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        return RsaKeyMaterial.FromKeyPair(rsa);
    }

    [Fact]
    public void GenerateTransactionKey_Returns16Bytes_AndDiffersBetweenCalls()
    {
        var first = EncryptionE002.GenerateTransactionKey();
        var second = EncryptionE002.GenerateTransactionKey();

        first.Length.Should().Be(EncryptionE002.TransactionKeySizeBytes);
        second.Length.Should().Be(EncryptionE002.TransactionKeySizeBytes);
        first.Should().NotEqual(second);
    }

    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrips()
    {
        var key = NewKeyPair();

        var encrypted = EncryptionE002.Encrypt(OrderData, key, E002);

        EncryptionE002.Decrypt(encrypted, key, E002).Should().Equal(OrderData);
    }

    [Fact]
    public void EncryptTransactionKey_Then_Decrypt_RoundTrips()
    {
        var key = NewKeyPair();
        var transactionKey = EncryptionE002.GenerateTransactionKey();

        var encryptedKey = EncryptionE002.EncryptTransactionKey(transactionKey, key, E002);

        EncryptionE002.DecryptTransactionKey(encryptedKey, key, E002).Should().Equal(transactionKey);
    }

    [Fact]
    public void EncryptOrderData_Then_Decrypt_RoundTrips()
    {
        var transactionKey = EncryptionE002.GenerateTransactionKey();

        var ciphertext = EncryptionE002.EncryptOrderData(OrderData, transactionKey);

        EncryptionE002.DecryptOrderData(ciphertext, transactionKey).Should().Equal(OrderData);
    }

    [Fact]
    public void Encrypt_WithPublicOnlyKey_ThenDecryptWithKeyPair_Succeeds()
    {
        var key = NewKeyPair();

        var encrypted = EncryptionE002.Encrypt(OrderData, key.ToPublicOnly(), E002);

        EncryptionE002.Decrypt(encrypted, key, E002).Should().Equal(OrderData);
    }

    [Fact]
    public void Encrypt_CarriesBothCiphertexts()
    {
        var key = NewKeyPair();

        var encrypted = EncryptionE002.Encrypt(OrderData, key, E002);

        // 2048-bit modulus → 256-byte RSA block.
        encrypted.EncryptedTransactionKey.Length.Should().Be(256);
        encrypted.EncryptedOrderDataBytes.Should().NotBeEmpty();
    }

    [Fact]
    public void KnownVector_Aes128Cbc_ProducesPinnedCiphertext()
    {
        var ciphertext = EncryptionE002.EncryptOrderData(KatPlaintext, KatTransactionKey);

        ciphertext.Should().Equal(Convert.FromBase64String(ExpectedAesCiphertextBase64));
        EncryptionE002.DecryptOrderData(ciphertext, KatTransactionKey).Should().Equal(KatPlaintext);
    }

    [Fact]
    public void KnownVector_Aes128Cbc_DecryptsPinnedCiphertext()
    {
        var ciphertext = Convert.FromBase64String(ExpectedAesCiphertextBase64);

        EncryptionE002.DecryptOrderData(ciphertext, KatTransactionKey).Should().Equal(KatPlaintext);
    }

    [Fact]
    public void EncryptTransactionKey_IsRandomised_YetBothDecrypt()
    {
        var key = NewKeyPair();
        var transactionKey = EncryptionE002.GenerateTransactionKey();

        var first = EncryptionE002.EncryptTransactionKey(transactionKey, key, E002);
        var second = EncryptionE002.EncryptTransactionKey(transactionKey, key, E002);

        first.Should().NotEqual(second);
        EncryptionE002.DecryptTransactionKey(first, key, E002).Should().Equal(transactionKey);
        EncryptionE002.DecryptTransactionKey(second, key, E002).Should().Equal(transactionKey);
    }

    [Fact]
    public void KnownVector_OaepRoundTrip_WithPinnedKey()
    {
        var key = RsaKeyImportExport.ImportPkcs8(Convert.FromBase64String(KnownPkcs8Base64));
        var transactionKey = EncryptionE002.GenerateTransactionKey();

        var encryptedKey = EncryptionE002.EncryptTransactionKey(transactionKey, key.ToPublicOnly(), E002);

        // OAEP is randomised, so the ciphertext cannot be byte-pinned; only its length and the
        // round-trip are stable.
        encryptedKey.Length.Should().Be(256);
        EncryptionE002.DecryptTransactionKey(encryptedKey, key, E002).Should().Equal(transactionKey);
    }

    [Fact]
    public void DecryptTransactionKey_WithPublicOnlyKey_Throws()
    {
        using var rsa = TestCertificates.CreateRsaKey(2048);
        var publicOnly = RsaKeyMaterial.FromPublicKey(rsa);
        var encryptedKey = EncryptionE002.EncryptTransactionKey(
            EncryptionE002.GenerateTransactionKey(), publicOnly, E002);

        var act = () => EncryptionE002.DecryptTransactionKey(encryptedKey, publicOnly, E002);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void Decrypt_WithPublicOnlyKey_Throws()
    {
        var key = NewKeyPair();
        var encrypted = EncryptionE002.Encrypt(OrderData, key, E002);

        var act = () => EncryptionE002.Decrypt(encrypted, key.ToPublicOnly(), E002);

        act.Should().Throw<KeyMaterialException>();
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var key = NewKeyPair();
        var other = NewKeyPair();
        var encrypted = EncryptionE002.Encrypt(OrderData, key, E002);

        var act = () => EncryptionE002.Decrypt(encrypted, other, E002);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void DecryptTransactionKey_TamperedCiphertext_Throws()
    {
        var key = NewKeyPair();
        var encryptedKey = EncryptionE002.EncryptTransactionKey(
            EncryptionE002.GenerateTransactionKey(), key, E002);
        encryptedKey[0] ^= 0xFF;

        var act = () => EncryptionE002.DecryptTransactionKey(encryptedKey, key, E002);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void DecryptOrderData_TamperedCiphertext_Throws()
    {
        // Uses the deterministic KAT ciphertext: flipping the last block byte breaks PKCS7 padding
        // (verified deterministic). CBC has no integrity, so tampering an earlier block would yield
        // garbled-but-valid plaintext instead — integrity is the bank signature's job (#19).
        var ciphertext = Convert.FromBase64String(ExpectedAesCiphertextBase64);
        ciphertext[^1] ^= 0xFF;

        var act = () => EncryptionE002.DecryptOrderData(ciphertext, KatTransactionKey);

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(32)]
    public void EncryptOrderData_WrongKeyLength_Throws(int keyLength)
    {
        var act = () => EncryptionE002.EncryptOrderData(OrderData, new byte[keyLength]);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(32)]
    public void DecryptOrderData_WrongKeyLength_Throws(int keyLength)
    {
        var ciphertext = Convert.FromBase64String(ExpectedAesCiphertextBase64);

        var act = () => EncryptionE002.DecryptOrderData(ciphertext, new byte[keyLength]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encrypt_NullKey_Throws()
    {
        var act = () => EncryptionE002.Encrypt(OrderData, null!, E002);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decrypt_NullKey_Throws()
    {
        var encrypted = new EncryptedOrderData(new byte[256], new byte[16]);

        var act = () => EncryptionE002.Decrypt(encrypted, null!, E002);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("A005")]
    [InlineData("X002")]
    [InlineData("E001")]
    [InlineData("A999")]
    public void EncryptTransactionKey_NonEncryptionOrUnknownVersion_Throws(string code)
    {
        var key = NewKeyPair();

        var act = () => EncryptionE002.EncryptTransactionKey(
            EncryptionE002.GenerateTransactionKey(), key, KeyVersion.Create(code));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Encrypt_DefaultVersion_Throws()
    {
        var key = NewKeyPair();

        var act = () => EncryptionE002.Encrypt(OrderData, key, default);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("A005")]
    [InlineData("X002")]
    [InlineData("E001")]
    public void Decrypt_NonEncryptionVersion_Throws(string code)
    {
        var key = NewKeyPair();
        var encrypted = EncryptionE002.Encrypt(OrderData, key, E002);

        var act = () => EncryptionE002.Decrypt(encrypted, key, KeyVersion.Create(code));

        act.Should().Throw<InvalidOperationException>();
    }
}
