using System.Security.Cryptography;
using System.Text;

namespace EBICO.Core.Crypto;

/// <summary>
/// Computes, renders and verifies the EBICS <i>public-key fingerprint</i> — the SHA-256 hash
/// of an RSA public key that appears in the INI initialisation letter, the HPB response and the
/// request header's <c>BankPubKeyDigests</c>. Stateless BCL wrappers
/// (<see href="../adr/0008-krypto-bibliothek.md">ADR-0008</see>) that build on the issue #18
/// key layer (<see cref="RsaKeyMaterial"/>).
/// </summary>
/// <remarks>
/// <para>
/// The hash input is the ASCII string <c>"&lt;exponent-hex&gt; &lt;modulus-hex&gt;"</c>: the public
/// exponent and modulus rendered as hexadecimal, leading zeros stripped, separated by a single
/// space. The bytes come from <see cref="RsaKeyMaterial.Exponent"/> / <see cref="RsaKeyMaterial.Modulus"/>,
/// which are already in EBICS canonical form (unsigned big-endian, no leading zero byte), so the
/// fingerprint agrees with the order-data layer on the exact bytes.
/// </para>
/// <para>
/// The fingerprint is version-agnostic: it never touches XML and only sees an
/// <see cref="RsaKeyMaterial"/>. The EBICS protocol version only decides <i>where</i> that material
/// comes from — H003/H004 from the <c>RSAKeyValue</c> (modulus/exponent) via
/// <see cref="RsaKeyImportExport.ImportRsaKeyValue"/>, H005 from the X.509 certificate via
/// <see cref="RsaKeyImportExport.ImportPublicKeyFromCertificate"/>. Both yield the same canonical
/// bytes and therefore the same fingerprint.
/// </para>
/// <para>
/// <b>⚠️ Spec-Vorbehalt:</b> the exact hash-input formatting (exponent-before-modulus order,
/// lowercase hex, leading-zero-<i>nibble</i> stripping, single-space separator) is an EBICS spec
/// detail not yet verified against the official specs (see the project conventions in
/// <c>CLAUDE.md</c> and <c>docs/protocol/public-key-fingerprint.md</c>). It is confined to the
/// single <see cref="NormalizeHashInput"/> seam.
/// </para>
/// </remarks>
public static class PublicKeyFingerprint
{
    /// <summary>The hash algorithm used for EBICS public-key fingerprints (SHA-256).</summary>
    public static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// The algorithm identifier written to <c>PubKeyDigestType/@Algorithm</c> on the wire
    /// (<c>http://www.w3.org/2001/04/xmlenc#sha256</c>), matching
    /// <see cref="AuthenticationSignature.DigestMethodAlgorithm"/>. Together with
    /// <see cref="Compute"/> this is all the M3 dispatch/onboarding layer needs to populate the
    /// generated digest elements (<c>EncryptionPubKeyDigest</c>, <c>BankPubKeyDigests</c>).
    /// </summary>
    public const string DigestAlgorithm = "http://www.w3.org/2001/04/xmlenc#sha256";

    private const int BytesPerLetterLine = 8;

    /// <summary>
    /// Computes the 32-byte SHA-256 fingerprint of an RSA public key: SHA-256 over the ASCII
    /// hash input produced by <see cref="BuildHashInput"/>.
    /// </summary>
    /// <param name="key">The key material (the public components are used; a private key is not required).</param>
    /// <returns>The 32-byte SHA-256 digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static byte[] Compute(RsaKeyMaterial key)
        => SHA256.HashData(Encoding.ASCII.GetBytes(BuildHashInput(key)));

    /// <summary>
    /// Builds the exact ASCII hash input — <c>"&lt;exponent-hex&gt; &lt;modulus-hex&gt;"</c> — that
    /// <see cref="Compute"/> hashes. Exposed so the INI letter and tests can pin the formatting
    /// (the single <see cref="NormalizeHashInput"/> seam) independently of the SHA-256 step.
    /// </summary>
    /// <param name="key">The key material.</param>
    /// <returns>The ASCII hash-input string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static string BuildHashInput(RsaKeyMaterial key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return NormalizeHashInput(key.Exponent, key.Modulus);
    }

    /// <summary>
    /// Verifies a counterparty-supplied fingerprint against the key's own fingerprint in constant
    /// time. A length or content mismatch yields <see langword="false"/> rather than an exception,
    /// so a malformed client-sent digest is a clean rejection on the server (like
    /// <see cref="BankSignature.Verify"/>).
    /// </summary>
    /// <remarks>
    /// Fingerprints are not secret, so timing is not security-critical here; the constant-time
    /// comparison follows the project convention (<c>docs/protocol/auth-signature-x002.md</c>).
    /// </remarks>
    /// <param name="key">The key whose fingerprint is the expected value.</param>
    /// <param name="expectedDigest">The digest received from the counterparty.</param>
    /// <returns><see langword="true"/> when the digests match; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static bool Verify(RsaKeyMaterial key, ReadOnlySpan<byte> expectedDigest)
    {
        ArgumentNullException.ThrowIfNull(key);
        return CryptographicOperations.FixedTimeEquals(Compute(key), expectedDigest);
    }

    /// <summary>
    /// Renders a digest for the INI initialisation letter: uppercase hexadecimal, byte pairs
    /// separated by a single space, eight bytes per line. Presentation only — the wire uses the
    /// base64 form of the raw <see cref="Compute"/> bytes, while the printed letter shows this
    /// grouped hex for human visual comparison at the bank.
    /// </summary>
    /// <param name="digest">The digest to render (typically the 32-byte output of <see cref="Compute"/>).</param>
    /// <returns>The grouped uppercase-hex representation.</returns>
    public static string ToLetterFormat(ReadOnlySpan<byte> digest)
    {
        var hex = Convert.ToHexString(digest); // uppercase, no separators, length == 2 * digest.Length
        var builder = new StringBuilder(hex.Length + (hex.Length / 2));
        for (var i = 0; i < digest.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(i % BytesPerLetterLine == 0 ? '\n' : ' ');
            }

            builder.Append(hex.AsSpan(i * 2, 2));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The single normalisation seam that turns the exponent and modulus into the ASCII hash
    /// input. Renders each as leading-zero-stripped lowercase hex and joins them, exponent first,
    /// with a single space.
    /// </summary>
    /// <remarks>
    /// <b>⚠️ Spec-Vorbehalt:</b> exponent-before-modulus order, lowercase hex and the
    /// leading-zero-nibble stripping are the unverified EBICS spec details; this is the one place
    /// to adjust them once the official specs are available. Both <see cref="Compute"/> and
    /// <see cref="BuildHashInput"/> route through here.
    /// </remarks>
    private static string NormalizeHashInput(ReadOnlyMemory<byte> exponent, ReadOnlyMemory<byte> modulus)
        => $"{ToStrippedLowerHex(exponent.Span)} {ToStrippedLowerHex(modulus.Span)}";

    /// <summary>
    /// Renders bytes as lowercase hex with leading zero <i>nibbles</i> stripped. This differs from
    /// the whole-byte trim in <see cref="RsaKeyMaterial"/>: the exponent 65537 is the canonical
    /// bytes <c>01 00 01</c> (no leading zero byte) but the hex <c>010001</c> carries a leading
    /// zero nibble, which is stripped to <c>10001</c>. The all-zero input collapses to <c>"0"</c>
    /// (defensive; an RSA exponent/modulus is never zero).
    /// </summary>
    private static string ToStrippedLowerHex(ReadOnlySpan<byte> value)
    {
        var hex = Convert.ToHexStringLower(value).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }
}
