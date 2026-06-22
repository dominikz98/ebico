using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EBICO.Core.Crypto;

/// <summary>
/// Imports and exports <see cref="RsaKeyMaterial"/> in the formats EBICS onboarding works
/// with: PKCS#8 (private keys), SubjectPublicKeyInfo / X.509 (public keys), PEM, and the
/// EBICS <c>RSAKeyValue</c> (modulus/exponent) representation. All methods translate the
/// BCL's <see cref="CryptographicException"/> into <see cref="KeyMaterialException"/> so
/// callers see one consistent failure type.
/// </summary>
/// <remarks>
/// This is representation and encoding only. No signing, encryption, hashing or certificate
/// chain/trust validation is performed here — those belong to the later M2 issues.
/// </remarks>
public static class RsaKeyImportExport
{
    /// <summary>Imports an unencrypted PKCS#8 private key (DER).</summary>
    /// <param name="pkcs8Der">The DER-encoded PKCS#8 private key.</param>
    /// <returns>Key material with a private key.</returns>
    /// <exception cref="KeyMaterialException">The data is malformed or not an RSA private key.</exception>
    public static RsaKeyMaterial ImportPkcs8(ReadOnlySpan<byte> pkcs8Der)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportPkcs8PrivateKey(pkcs8Der, out _);
        }
        catch (CryptographicException ex)
        {
            throw new KeyMaterialException(
                "Failed to import the PKCS#8 private key; the data is malformed or not an RSA private key.", ex);
        }

        return RsaKeyMaterial.FromKeyPair(rsa);
    }

    /// <summary>Exports the private key as unencrypted PKCS#8 (DER).</summary>
    /// <param name="material">The key material; must contain a private key.</param>
    /// <returns>The DER-encoded PKCS#8 private key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="material"/> has no private key.</exception>
    public static byte[] ExportPkcs8(RsaKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!material.HasPrivateKey)
        {
            throw new KeyMaterialException("Cannot export PKCS#8: the key material has no private key.");
        }

        using var rsa = material.CreateRsa();
        return rsa.ExportPkcs8PrivateKey();
    }

    /// <summary>Imports a public key from a SubjectPublicKeyInfo structure (DER).</summary>
    /// <param name="spkiDer">The DER-encoded SubjectPublicKeyInfo.</param>
    /// <returns>Public-only key material.</returns>
    /// <exception cref="KeyMaterialException">The data is malformed or not an RSA public key.</exception>
    public static RsaKeyMaterial ImportSubjectPublicKeyInfo(ReadOnlySpan<byte> spkiDer)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(spkiDer, out _);
        }
        catch (CryptographicException ex)
        {
            throw new KeyMaterialException(
                "Failed to import the SubjectPublicKeyInfo; the data is malformed or not an RSA public key.", ex);
        }

        return RsaKeyMaterial.FromPublicKey(rsa);
    }

    /// <summary>Exports the public key as a SubjectPublicKeyInfo structure (DER).</summary>
    /// <param name="material">The key material.</param>
    /// <returns>The DER-encoded SubjectPublicKeyInfo.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    public static byte[] ExportSubjectPublicKeyInfo(RsaKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        using var rsa = material.CreateRsa();
        return rsa.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// Extracts the RSA public key from an X.509 certificate. This reads the public key only;
    /// it performs no chain building or trust validation (that is a separate concern).
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>Public-only key material.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="certificate"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException">The certificate has no RSA public key.</exception>
    public static RsaKeyMaterial ImportPublicKeyFromCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using var rsa = certificate.GetRSAPublicKey()
            ?? throw new KeyMaterialException("The certificate does not contain an RSA public key.");
        return RsaKeyMaterial.FromPublicKey(rsa);
    }

    /// <summary>
    /// Imports an RSA key from PEM text. Accepts both public (<c>PUBLIC KEY</c>/<c>RSA PUBLIC KEY</c>)
    /// and private (<c>PRIVATE KEY</c>/<c>RSA PRIVATE KEY</c>) labels; the result's
    /// <see cref="RsaKeyMaterial.HasPrivateKey"/> reflects which was supplied.
    /// </summary>
    /// <param name="pem">The PEM-encoded key.</param>
    /// <returns>The imported key material.</returns>
    /// <exception cref="KeyMaterialException">No supported RSA key was found or the data is malformed.</exception>
    public static RsaKeyMaterial ImportFromPem(ReadOnlySpan<char> pem)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new KeyMaterialException(
                "Failed to import the PEM-encoded key; no supported RSA key was found or the data is malformed.", ex);
        }

        return TryHasPrivateKey(rsa)
            ? RsaKeyMaterial.FromKeyPair(rsa)
            : RsaKeyMaterial.FromPublicKey(rsa);
    }

    /// <summary>Exports the public key as PEM (<c>PUBLIC KEY</c>, SubjectPublicKeyInfo).</summary>
    /// <param name="material">The key material.</param>
    /// <returns>The PEM-encoded public key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    public static string ExportPublicKeyPem(RsaKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        using var rsa = material.CreateRsa();
        return rsa.ExportSubjectPublicKeyInfoPem();
    }

    /// <summary>Exports the private key as PEM (<c>PRIVATE KEY</c>, unencrypted PKCS#8).</summary>
    /// <param name="material">The key material; must contain a private key.</param>
    /// <returns>The PEM-encoded private key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyMaterialException"><paramref name="material"/> has no private key.</exception>
    public static string ExportPkcs8Pem(RsaKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!material.HasPrivateKey)
        {
            throw new KeyMaterialException("Cannot export PKCS#8 PEM: the key material has no private key.");
        }

        using var rsa = material.CreateRsa();
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>Builds public-only material from an EBICS <c>RSAKeyValue</c> (modulus/exponent).</summary>
    /// <param name="modulus">The modulus, unsigned big-endian.</param>
    /// <param name="exponent">The public exponent, unsigned big-endian.</param>
    /// <returns>Public-only key material.</returns>
    /// <exception cref="KeyMaterialException">The modulus or exponent is empty, or the key is too small.</exception>
    public static RsaKeyMaterial ImportRsaKeyValue(ReadOnlySpan<byte> modulus, ReadOnlySpan<byte> exponent)
        => RsaKeyMaterial.FromModulusExponent(modulus, exponent);

    /// <summary>
    /// Returns the EBICS <c>RSAKeyValue</c> representation (modulus and exponent bytes, in
    /// canonical big-endian form) for the public part of the material.
    /// </summary>
    /// <param name="material">The key material.</param>
    /// <returns>The modulus and exponent byte arrays.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    public static (byte[] Modulus, byte[] Exponent) ExportRsaKeyValue(RsaKeyMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return (material.Modulus.ToArray(), material.Exponent.ToArray());
    }

    private static bool TryHasPrivateKey(RSA rsa)
    {
        try
        {
            _ = rsa.ExportParameters(includePrivateParameters: true);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
