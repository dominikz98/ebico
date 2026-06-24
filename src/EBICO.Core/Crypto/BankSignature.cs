using System.Security.Cryptography;

namespace EBICO.Core.Crypto;

/// <summary>
/// Computes and verifies the EBICS bank-technical (authorising) signature over order data,
/// for the signature key versions <c>A005</c> (RSASSA-PKCS1-v1_5) and <c>A006</c>
/// (RSASSA-PSS), both over SHA-256. Stateless BCL wrappers
/// (<see href="../adr/0008-krypto-bibliothek.md">ADR-0008</see>) that build on the issue #18
/// key layer (<see cref="RsaKeyMaterial"/>, <see cref="KeyVersions"/>).
/// </summary>
/// <remarks>
/// <para>
/// The signing input is the SHA-256 <i>order hash</i> (<see cref="ComputeOrderHash"/>), not the
/// raw order data: the hash is formed once, is inspectable, and is reused by the order-data and
/// fingerprint layers. The padding scheme is taken from the <see cref="KeyVersions"/> registry
/// (<see cref="KeyVersionInfo.PaddingIntent"/>), never hard-coded — A005 maps to
/// <see cref="RSASignaturePadding.Pkcs1"/>, A006 to <see cref="RSASignaturePadding.Pss"/>.
/// </para>
/// <para>
/// This layer is deliberately policy-free: it does <b>not</b> check whether a key version is
/// permitted for a given EBICS protocol version (A006 with H003, say) — that is
/// <see cref="KeyVersions.EnsurePermitted"/>'s job in the dispatch/onboarding layer. It only
/// rejects versions that are not a known <see cref="KeyPurpose.Signature"/> version.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the exact order-data normalisation that precedes the hash is an
/// EBICS spec detail not yet verified against the official specs (see the project conventions in
/// <c>CLAUDE.md</c> and <c>docs/protocol/bank-signature.md</c>). It is confined to the single
/// <c>NormalizeOrderData</c> seam; self-consistent sign &#8594; verify round-trips hold
/// regardless of that choice.
/// </para>
/// </remarks>
public static class BankSignature
{
    /// <summary>The hash algorithm used for the EBICS bank-technical signature (SHA-256).</summary>
    public static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Computes the EBICS order hash: SHA-256 over the (normalised) order data. This is the
    /// value the A005/A006 signature is computed over and is exposed so that the order-data and
    /// fingerprint layers can agree on the exact bytes.
    /// </summary>
    /// <param name="orderData">The order data to hash.</param>
    /// <returns>The 32-byte SHA-256 digest.</returns>
    public static byte[] ComputeOrderHash(ReadOnlySpan<byte> orderData)
        => SHA256.HashData(NormalizeOrderData(orderData));

    /// <summary>
    /// Signs order data with the given signature key version (A005 or A006). The order hash is
    /// computed internally via <see cref="ComputeOrderHash"/> and signed.
    /// </summary>
    /// <param name="orderData">The raw order data to sign.</param>
    /// <param name="key">The signer's key material; must contain a private key.</param>
    /// <param name="version">The signature key version (must resolve to a known A00x version).</param>
    /// <returns>The RSA signature bytes (length equals the modulus size).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="key"/> has no private key.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known signature version.</exception>
    public static byte[] Sign(ReadOnlySpan<byte> orderData, RsaKeyMaterial key, KeyVersion version)
        => SignHash(ComputeOrderHash(orderData), key, version);

    /// <summary>
    /// Signs a pre-computed order hash with the given signature key version (A005 or A006).
    /// </summary>
    /// <param name="orderHash">The SHA-256 order hash (see <see cref="ComputeOrderHash"/>).</param>
    /// <param name="key">The signer's key material; must contain a private key.</param>
    /// <param name="version">The signature key version (must resolve to a known A00x version).</param>
    /// <returns>The RSA signature bytes (length equals the modulus size).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="key"/> has no private key.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known signature version.</exception>
    public static byte[] SignHash(ReadOnlySpan<byte> orderHash, RsaKeyMaterial key, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!key.HasPrivateKey)
        {
            throw new KeyMaterialException("Cannot sign: the key material has no private key.");
        }

        var padding = ResolveSignaturePadding(version);
        using var rsa = key.CreateRsa();
        return rsa.SignHash(orderHash.ToArray(), HashAlgorithm, padding);
    }

    /// <summary>
    /// Verifies a bank-technical signature over order data. The order hash is computed internally
    /// via <see cref="ComputeOrderHash"/>.
    /// </summary>
    /// <param name="orderData">The raw order data that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="key">The signer's public key material (a private key is not required).</param>
    /// <param name="version">The signature key version (must resolve to a known A00x version).</param>
    /// <returns><see langword="true"/> when the signature is valid; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known signature version.</exception>
    public static bool Verify(
        ReadOnlySpan<byte> orderData, ReadOnlySpan<byte> signature, RsaKeyMaterial key, KeyVersion version)
        => VerifyHash(ComputeOrderHash(orderData), signature, key, version);

    /// <summary>
    /// Verifies a bank-technical signature over a pre-computed order hash. Bad signatures (wrong
    /// key, tampered data/signature, wrong length) yield <see langword="false"/> rather than an
    /// exception, so a malformed client signature is a clean rejection on the server.
    /// </summary>
    /// <param name="orderHash">The SHA-256 order hash that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="key">The signer's public key material (a private key is not required).</param>
    /// <param name="version">The signature key version (must resolve to a known A00x version).</param>
    /// <returns><see langword="true"/> when the signature is valid; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="version"/> is not a known signature version.</exception>
    public static bool VerifyHash(
        ReadOnlySpan<byte> orderHash, ReadOnlySpan<byte> signature, RsaKeyMaterial key, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(key);
        var padding = ResolveSignaturePadding(version);
        using var rsa = key.CreateRsa();
        return rsa.VerifyHash(orderHash.ToArray(), signature.ToArray(), HashAlgorithm, padding);
    }

    /// <summary>
    /// Maps a signature key version to its BCL RSA padding by consulting the
    /// <see cref="KeyVersions"/> registry — A005 (<see cref="RsaPaddingScheme.Pkcs1V15"/>) →
    /// <see cref="RSASignaturePadding.Pkcs1"/>, A006 (<see cref="RsaPaddingScheme.Pss"/>) →
    /// <see cref="RSASignaturePadding.Pss"/>.
    /// </summary>
    private static RSASignaturePadding ResolveSignaturePadding(KeyVersion version)
    {
        if (!KeyVersions.TryGet(version, out var info) || info.Purpose != KeyPurpose.Signature)
        {
            throw new InvalidOperationException(
                $"Key version '{version.Value}' is not a known EBICS signature version (expected A005 or A006).");
        }

        return info.PaddingIntent switch
        {
            RsaPaddingScheme.Pkcs1V15 => RSASignaturePadding.Pkcs1,
            RsaPaddingScheme.Pss => RSASignaturePadding.Pss,
            _ => throw new InvalidOperationException(
                $"Signature version '{version.Value}' implies a non-signature padding scheme ({info.PaddingIntent})."),
        };
    }

    /// <summary>
    /// The single normalisation seam applied to order data before hashing. Currently the
    /// identity (pass-through).
    /// </summary>
    /// <remarks>
    /// <b>⚠️ Spec-Vorbehalt:</b> if the official EBICS specs mandate a canonicalisation before
    /// hashing (e.g. line-ending normalisation for certain formats), this is the one place to
    /// implement it. Both <see cref="Sign"/> and <see cref="Verify"/> route through here, so
    /// round-trips stay invariant to the choice.
    /// </remarks>
    private static byte[] NormalizeOrderData(ReadOnlySpan<byte> orderData) => orderData.ToArray();
}
