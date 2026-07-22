using System.Security.Cryptography;

namespace EBICO.Core.Crypto;

/// <summary>
/// An immutable holder for RSA key material — a public key, optionally with its private
/// components. EBICS keys (signature <c>A</c>, encryption <c>E</c>, authentication <c>X</c>)
/// are all RSA, so this single container backs all three purposes.
/// </summary>
/// <remarks>
/// <para>
/// The material is stored as cloned <see cref="RSAParameters"/> rather than a live
/// <see cref="RSA"/> instance: that keeps the container immutable and free of
/// <see cref="IDisposable"/> concerns. Call <see cref="CreateRsa"/> to obtain a fresh
/// <see cref="RSA"/> for a cryptographic operation; the caller owns and disposes it.
/// </para>
/// <para>
/// <see cref="Modulus"/> and <see cref="Exponent"/> are exposed in EBICS canonical form
/// (unsigned big-endian, no leading zero byte) so that downstream key fingerprinting and
/// order-data assembly agree on the bytes.
/// </para>
/// </remarks>
public sealed class RsaKeyMaterial
{
    /// <summary>
    /// Minimum accepted RSA key size in bits. EBICS allows 1536–4096; EBICO requires at
    /// least 2048 by default. Revisable policy.
    /// </summary>
    public const int MinKeySizeBits = 2048;

    private readonly byte[] _modulus;
    private readonly byte[] _exponent;
    private readonly RSAParameters _parameters;

    private RsaKeyMaterial(RSAParameters parameters, bool hasPrivateKey)
    {
        _modulus = Trim(parameters.Modulus) ?? throw new KeyMaterialException("RSA modulus is missing.");
        _exponent = Trim(parameters.Exponent) ?? throw new KeyMaterialException("RSA exponent is missing.");

        KeySizeBits = _modulus.Length * 8;
        if (KeySizeBits < MinKeySizeBits)
        {
            throw new KeyMaterialException(
                $"RSA key size {KeySizeBits} bits is below the minimum of {MinKeySizeBits} bits.");
        }

        // Import from the *canonical* modulus/exponent, not from the bytes as supplied (issue #117).
        // XML-DSig CryptoBinary carries no leading zero byte, but real clients (node-ebics-client and
        // anything else emitting an ASN.1 INTEGER) prefix one whenever the high bit is set. Passing
        // those 257 bytes straight to RSA.ImportParameters yields a 2056-bit key whose OAEP/PKCS#1
        // operations then fail — while KeySizeBits and the fingerprint bytes, which are computed off
        // the trimmed form, claim 2048. Normalizing here keeps the two views consistent.
        var canonical = Clone(parameters);
        canonical.Modulus = CopyOf(_modulus);
        canonical.Exponent = CopyOf(_exponent);
        _parameters = canonical;
        HasPrivateKey = hasPrivateKey;
    }

    /// <summary>Whether private key components are present (a full key pair, not a public key only).</summary>
    public bool HasPrivateKey { get; }

    /// <summary>The RSA key size in bits, derived from the modulus length.</summary>
    public int KeySizeBits { get; }

    /// <summary>The RSA modulus in EBICS canonical form (unsigned big-endian, no leading zero).</summary>
    public ReadOnlyMemory<byte> Modulus => _modulus;

    /// <summary>The RSA public exponent in EBICS canonical form (unsigned big-endian, no leading zero).</summary>
    public ReadOnlyMemory<byte> Exponent => _exponent;

    /// <summary>Captures the public-only material of an RSA instance.</summary>
    /// <param name="rsa">The RSA instance.</param>
    /// <returns>A public-only <see cref="RsaKeyMaterial"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rsa"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException">The key is smaller than <see cref="MinKeySizeBits"/>.</exception>
    public static RsaKeyMaterial FromPublicKey(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        return new RsaKeyMaterial(parameters, hasPrivateKey: false);
    }

    /// <summary>Captures the full key pair (public + private components) of an RSA instance.</summary>
    /// <param name="rsa">The RSA instance, which must contain a private key.</param>
    /// <returns>A <see cref="RsaKeyMaterial"/> with <see cref="HasPrivateKey"/> set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rsa"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException">
    /// <paramref name="rsa"/> has no private key, or the key is smaller than <see cref="MinKeySizeBits"/>.
    /// </exception>
    public static RsaKeyMaterial FromKeyPair(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        RSAParameters parameters;
        try
        {
            parameters = rsa.ExportParameters(includePrivateParameters: true);
        }
        catch (CryptographicException ex)
        {
            throw new KeyMaterialException("The RSA instance does not contain a private key.", ex);
        }

        return new RsaKeyMaterial(parameters, hasPrivateKey: true);
    }

    /// <summary>
    /// Builds public-only material from an EBICS <c>RSAKeyValue</c> (raw modulus and exponent bytes).
    /// </summary>
    /// <param name="modulus">The modulus, unsigned big-endian.</param>
    /// <param name="exponent">The public exponent, unsigned big-endian.</param>
    /// <returns>A public-only <see cref="RsaKeyMaterial"/>.</returns>
    /// <exception cref="KeyMaterialException">
    /// The modulus or exponent is empty, or the key is smaller than <see cref="MinKeySizeBits"/>.
    /// </exception>
    public static RsaKeyMaterial FromModulusExponent(ReadOnlySpan<byte> modulus, ReadOnlySpan<byte> exponent)
    {
        var parameters = new RSAParameters
        {
            Modulus = modulus.ToArray(),
            Exponent = exponent.ToArray(),
        };

        return new RsaKeyMaterial(parameters, hasPrivateKey: false);
    }

    /// <summary>
    /// Generates a fresh RSA key pair of the given size and captures it as full key material
    /// (public + private). This is the client-side key generation used by EBICS onboarding to
    /// create the subscriber's signature (<c>A00x</c>), encryption (<c>E002</c>) and
    /// authentication (<c>X002</c>) keys.
    /// </summary>
    /// <param name="keySizeBits">The RSA key size in bits; defaults to <see cref="MinKeySizeBits"/>.</param>
    /// <returns>A <see cref="RsaKeyMaterial"/> with <see cref="HasPrivateKey"/> set.</returns>
    /// <exception cref="KeyMaterialException"><paramref name="keySizeBits"/> is below <see cref="MinKeySizeBits"/>.</exception>
    public static RsaKeyMaterial Generate(int keySizeBits = MinKeySizeBits)
    {
        // Validate before generating: RSA.Create would happily produce an undersized key, and we
        // want a clear KeyMaterialException rather than the cost of generating a key we then reject.
        if (keySizeBits < MinKeySizeBits)
        {
            throw new KeyMaterialException(
                $"RSA key size {keySizeBits} bits is below the minimum of {MinKeySizeBits} bits.");
        }

        using var rsa = RSA.Create(keySizeBits);
        return FromKeyPair(rsa);
    }

    /// <summary>
    /// Creates a fresh <see cref="RSA"/> instance carrying this material. The caller owns and
    /// must dispose the returned instance.
    /// </summary>
    /// <returns>A new <see cref="RSA"/> instance.</returns>
    /// <exception cref="KeyMaterialException">The stored parameters cannot be imported.</exception>
    public RSA CreateRsa()
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportParameters(_parameters);
            return rsa;
        }
        catch (CryptographicException ex)
        {
            rsa.Dispose();
            throw new KeyMaterialException("The stored RSA parameters could not be imported.", ex);
        }
    }

    /// <summary>
    /// Returns a public-only projection of this material, dropping any private components.
    /// Returns the same instance when it is already public-only.
    /// </summary>
    /// <returns>A public-only <see cref="RsaKeyMaterial"/>.</returns>
    public RsaKeyMaterial ToPublicOnly()
    {
        if (!HasPrivateKey)
        {
            return this;
        }

        var publicParameters = new RSAParameters
        {
            Modulus = (byte[])_parameters.Modulus!.Clone(),
            Exponent = (byte[])_parameters.Exponent!.Clone(),
        };

        return new RsaKeyMaterial(publicParameters, hasPrivateKey: false);
    }

    private static byte[]? Trim(byte[]? value)
    {
        if (value is null || value.Length == 0)
        {
            return null;
        }

        var start = 0;
        while (start < value.Length - 1 && value[start] == 0)
        {
            start++;
        }

        var result = new byte[value.Length - start];
        Array.Copy(value, start, result, 0, result.Length);
        return result;
    }

    private static RSAParameters Clone(RSAParameters p) => new()
    {
        Modulus = CopyOf(p.Modulus),
        Exponent = CopyOf(p.Exponent),
        D = CopyOf(p.D),
        P = CopyOf(p.P),
        Q = CopyOf(p.Q),
        DP = CopyOf(p.DP),
        DQ = CopyOf(p.DQ),
        InverseQ = CopyOf(p.InverseQ),
    };

    private static byte[]? CopyOf(byte[]? value) => value is null ? null : (byte[])value.Clone();
}
