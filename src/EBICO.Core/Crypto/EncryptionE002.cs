using System.Security.Cryptography;

namespace EBICO.Core.Crypto;

/// <summary>
/// Performs the EBICS transport encryption (encryption key version <c>E002</c>): a hybrid scheme
/// that encrypts the order data symmetrically with a one-time AES-128-CBC <i>transaction key</i>
/// and encrypts that transaction key asymmetrically with the recipient's public encryption key
/// using RSAES-OAEP over SHA-256. Stateless BCL wrappers
/// (<see href="../adr/0008-krypto-bibliothek.md">ADR-0008</see>) that build on the issue #18 key
/// layer (<see cref="RsaKeyMaterial"/>, <see cref="KeyVersions"/>).
/// </summary>
/// <remarks>
/// <para>
/// The two halves are exposed as primitives — <see cref="EncryptTransactionKey"/> /
/// <see cref="DecryptTransactionKey"/> (RSA-OAEP over the symmetric key) and
/// <see cref="EncryptOrderData"/> / <see cref="DecryptOrderData"/> (AES-128-CBC over the data) —
/// plus the combined <see cref="Encrypt"/> / <see cref="Decrypt"/> hybrid flow that the
/// data-transfer layer assembles onto the wire. The RSA padding is taken from the
/// <see cref="KeyVersions"/> registry (<see cref="KeyVersionInfo.PaddingIntent"/>), never
/// hard-coded — E002 maps to <see cref="RsaPaddingScheme.Oaep"/> →
/// <see cref="RSAEncryptionPadding.OaepSHA256"/>.
/// </para>
/// <para>
/// Like the signature layer this is deliberately policy-free: it does <b>not</b> check whether
/// E002 is permitted for a given EBICS protocol version — that is
/// <see cref="KeyVersions.EnsurePermitted"/>'s job in the dispatch/onboarding layer. It only
/// rejects versions that are not a known <see cref="KeyPurpose.Encryption"/> version with an OAEP
/// padding intent. Unlike <see cref="BankSignature.Verify"/> there is no
/// <see langword="false"/>-instead-of-throw path: encryption/decryption has no boolean verdict, so
/// every failure throws. E002 provides confidentiality only; integrity/authenticity comes from the
/// bank-technical signature (issue #19), not from this layer.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the EBICS encryption version <c>E002</c> has, in some historical spec
/// revisions, encrypted the transaction key with RSAES-PKCS1-v1_5 rather than OAEP. EBICO follows
/// the registry intent (OAEP-SHA256, per this issue) and the official Annex is to be verified once
/// available (see <c>CLAUDE.md</c> and <c>docs/protocol/encryption-e002.md</c>). Because the
/// padding is registry-driven, switching it is a one-line change in <see cref="KeyVersions"/>, not
/// an edit to this primitive. The symmetric IV and padding are likewise confined to a single seam
/// (<see cref="TransactionIv"/>, <see cref="SymmetricPadding"/>); self-consistent encrypt &#8594;
/// decrypt round-trips hold regardless of those choices.
/// </para>
/// </remarks>
public static class EncryptionE002
{
    /// <summary>
    /// The size in bytes of the AES-128 transaction key (16). The AES block size and the CBC
    /// initialisation vector share this length.
    /// </summary>
    public const int TransactionKeySizeBytes = 16;

    /// <summary>
    /// Generates a fresh, cryptographically random AES-128 transaction key
    /// (<see cref="TransactionKeySizeBytes"/> bytes). The caller owns the returned secret.
    /// </summary>
    /// <returns>A new 16-byte transaction key.</returns>
    public static byte[] GenerateTransactionKey()
        => RandomNumberGenerator.GetBytes(TransactionKeySizeBytes);

    /// <summary>
    /// Encrypts a transaction key with the recipient's public encryption key using RSAES-OAEP over
    /// SHA-256 (E002). Only the public key is required.
    /// </summary>
    /// <param name="transactionKey">The symmetric transaction key to encrypt.</param>
    /// <param name="key">The recipient's encryption key material (public key suffices).</param>
    /// <param name="version">The encryption key version (must resolve to E002).</param>
    /// <returns>The RSA-OAEP-encrypted transaction key (length equals the modulus size).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known E002 encryption version.</exception>
    /// <exception cref="CryptographicException">The RSA operation fails (e.g. the key is too small for the input).</exception>
    public static byte[] EncryptTransactionKey(
        ReadOnlySpan<byte> transactionKey, RsaKeyMaterial key, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(key);
        var padding = ResolveEncryptionPadding(version);
        using var rsa = key.CreateRsa();
        return rsa.Encrypt(transactionKey.ToArray(), padding);
    }

    /// <summary>
    /// Decrypts a transaction key that was encrypted with RSAES-OAEP over SHA-256 (E002). The
    /// recipient's private key is required.
    /// </summary>
    /// <param name="encryptedTransactionKey">The RSA-OAEP-encrypted transaction key.</param>
    /// <param name="key">The recipient's key material; must contain a private key.</param>
    /// <param name="version">The encryption key version (must resolve to E002).</param>
    /// <returns>The decrypted transaction key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="key"/> has no private key.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known E002 encryption version.</exception>
    /// <exception cref="CryptographicException">The ciphertext is invalid for this key (wrong key, tampered, or bad padding).</exception>
    public static byte[] DecryptTransactionKey(
        ReadOnlySpan<byte> encryptedTransactionKey, RsaKeyMaterial key, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!key.HasPrivateKey)
        {
            throw new KeyMaterialException("Cannot decrypt: the key material has no private key.");
        }

        var padding = ResolveEncryptionPadding(version);
        using var rsa = key.CreateRsa();
        return rsa.Decrypt(encryptedTransactionKey.ToArray(), padding);
    }

    /// <summary>
    /// Encrypts order data with AES-128-CBC under the given transaction key (E002 symmetric leg).
    /// </summary>
    /// <param name="orderData">The order data to encrypt.</param>
    /// <param name="transactionKey">The AES-128 transaction key (<see cref="TransactionKeySizeBytes"/> bytes).</param>
    /// <returns>The AES-128-CBC ciphertext.</returns>
    /// <exception cref="ArgumentException"><paramref name="transactionKey"/> is not <see cref="TransactionKeySizeBytes"/> bytes long.</exception>
    public static byte[] EncryptOrderData(ReadOnlySpan<byte> orderData, ReadOnlySpan<byte> transactionKey)
    {
        EnsureTransactionKeyLength(transactionKey);
        using var aes = Aes.Create();
        aes.Key = transactionKey.ToArray();
        return aes.EncryptCbc(orderData, TransactionIv, SymmetricPadding);
    }

    /// <summary>
    /// Decrypts AES-128-CBC order-data ciphertext under the given transaction key (E002 symmetric leg).
    /// </summary>
    /// <param name="ciphertext">The AES-128-CBC ciphertext.</param>
    /// <param name="transactionKey">The AES-128 transaction key (<see cref="TransactionKeySizeBytes"/> bytes).</param>
    /// <returns>The decrypted order data.</returns>
    /// <exception cref="ArgumentException"><paramref name="transactionKey"/> is not <see cref="TransactionKeySizeBytes"/> bytes long.</exception>
    /// <exception cref="CryptographicException">The ciphertext length is not a block multiple or the padding is invalid.</exception>
    public static byte[] DecryptOrderData(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> transactionKey)
    {
        EnsureTransactionKeyLength(transactionKey);
        using var aes = Aes.Create();
        aes.Key = transactionKey.ToArray();
        return aes.DecryptCbc(ciphertext, TransactionIv, SymmetricPadding);
    }

    /// <summary>
    /// Performs the full E002 hybrid encryption: generates a one-time transaction key, encrypts the
    /// order data with AES-128-CBC, and encrypts the transaction key with RSAES-OAEP for the
    /// recipient. Only the recipient's public key is required.
    /// </summary>
    /// <param name="orderData">The order data to encrypt.</param>
    /// <param name="recipientKey">The recipient's encryption key material (public key suffices).</param>
    /// <param name="version">The encryption key version (must resolve to E002).</param>
    /// <returns>The encrypted transaction key and the encrypted order data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recipientKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known E002 encryption version.</exception>
    /// <exception cref="CryptographicException">The RSA operation fails.</exception>
    public static EncryptedOrderData Encrypt(
        ReadOnlySpan<byte> orderData, RsaKeyMaterial recipientKey, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(recipientKey);
        var transactionKey = GenerateTransactionKey();
        var encryptedOrderData = EncryptOrderData(orderData, transactionKey);
        var encryptedTransactionKey = EncryptTransactionKey(transactionKey, recipientKey, version);
        return new EncryptedOrderData(encryptedTransactionKey, encryptedOrderData);
    }

    /// <summary>
    /// Reverses <see cref="Encrypt"/>: decrypts the transaction key with the recipient's private
    /// key and then decrypts the order data. The recipient's private key is required.
    /// </summary>
    /// <param name="encrypted">The encrypted transaction key and order data.</param>
    /// <param name="recipientKey">The recipient's key material; must contain a private key.</param>
    /// <param name="version">The encryption key version (must resolve to E002).</param>
    /// <returns>The decrypted order data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recipientKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="recipientKey"/> has no private key.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known E002 encryption version.</exception>
    /// <exception cref="CryptographicException">A ciphertext is invalid for this key.</exception>
    public static byte[] Decrypt(EncryptedOrderData encrypted, RsaKeyMaterial recipientKey, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(recipientKey);
        var transactionKey = DecryptTransactionKey(encrypted.EncryptedTransactionKey, recipientKey, version);
        return DecryptOrderData(encrypted.EncryptedOrderDataBytes, transactionKey);
    }

    /// <summary>
    /// Maps an encryption key version to its BCL RSA padding by consulting the
    /// <see cref="KeyVersions"/> registry — E002 (<see cref="RsaPaddingScheme.Oaep"/>) →
    /// <see cref="RSAEncryptionPadding.OaepSHA256"/>.
    /// </summary>
    private static RSAEncryptionPadding ResolveEncryptionPadding(KeyVersion version)
    {
        if (!KeyVersions.TryGet(version, out var info) || info.Purpose != KeyPurpose.Encryption)
        {
            throw new InvalidOperationException(
                $"Key version '{version.Value}' is not a known EBICS encryption version (expected E002).");
        }

        return info.PaddingIntent switch
        {
            RsaPaddingScheme.Oaep => RSAEncryptionPadding.OaepSHA256,
            _ => throw new InvalidOperationException(
                $"Encryption version '{version.Value}' implies an unsupported padding scheme ({info.PaddingIntent}) for E002."),
        };
    }

    private static void EnsureTransactionKeyLength(ReadOnlySpan<byte> transactionKey)
    {
        if (transactionKey.Length != TransactionKeySizeBytes)
        {
            throw new ArgumentException(
                $"The AES-128 transaction key must be exactly {TransactionKeySizeBytes} bytes, but was {transactionKey.Length}.",
                nameof(transactionKey));
        }
    }

    /// <summary>
    /// The CBC initialisation vector for the order-data encryption: 16 zero bytes (the EBICS E002
    /// convention).
    /// </summary>
    /// <remarks>
    /// <b>⚠️ Spec-Vorbehalt:</b> the zero IV and the <see cref="SymmetricPadding"/> are EBICS spec
    /// details not yet verified against the official specs (vgl. <c>CLAUDE.md</c>). They are
    /// confined to this seam; because both <see cref="EncryptOrderData"/> and
    /// <see cref="DecryptOrderData"/> route through here, round-trips stay invariant to the choice.
    /// </remarks>
    private static readonly byte[] TransactionIv = new byte[TransactionKeySizeBytes];

    /// <summary>The symmetric padding mode for the order-data encryption. See <see cref="TransactionIv"/> for the spec caveat.</summary>
    private const PaddingMode SymmetricPadding = PaddingMode.PKCS7;
}

/// <summary>
/// The two ciphertext outputs of an E002 hybrid encryption (see <see cref="EncryptionE002.Encrypt"/>):
/// the RSA-OAEP-encrypted transaction key (the <c>DataEncryptionInfo/TransactionKey</c> element on
/// the wire) and the AES-128-CBC-encrypted order data.
/// </summary>
/// <param name="EncryptedTransactionKey">The RSA-OAEP-encrypted transaction key.</param>
/// <param name="EncryptedOrderDataBytes">The AES-128-CBC-encrypted order data.</param>
public readonly record struct EncryptedOrderData(byte[] EncryptedTransactionKey, byte[] EncryptedOrderDataBytes);
