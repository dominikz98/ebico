namespace EBICO.Core.Crypto;

/// <summary>
/// Base type for errors raised by the EBICO cryptographic key layer (key-version parsing,
/// key material import/export, and per-version key-version policy). Kept separate from
/// the domain model's <c>EbicsDomainException</c> so callers can catch crypto failures
/// distinctly.
/// </summary>
public class EbicsCryptoException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EbicsCryptoException"/> class.</summary>
    public EbicsCryptoException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsCryptoException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsCryptoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when a key-version code is not well-formed — that is, not a letter
/// <c>A</c>/<c>E</c>/<c>X</c> followed by exactly three digits (e.g. <c>"A005"</c>,
/// <c>"E002"</c>, <c>"X002"</c>).
/// </summary>
/// <remarks>
/// A well-formed but unknown code (e.g. <c>"A999"</c>) does <b>not</b> raise this — it is
/// accepted by <see cref="KeyVersion.Create(string)"/> and merely fails to resolve via
/// <see cref="KeyVersions.TryGet(KeyVersion, out KeyVersionInfo?)"/>.
/// </remarks>
public class InvalidKeyVersionException : EbicsCryptoException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidKeyVersionException"/> class.</summary>
    public InvalidKeyVersionException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public InvalidKeyVersionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public InvalidKeyVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised for problems with RSA key material: malformed PKCS#8 / SubjectPublicKeyInfo /
/// PEM / certificate input, a missing private key when one is required, or a key whose
/// size is below the accepted minimum.
/// </summary>
public class KeyMaterialException : EbicsCryptoException
{
    /// <summary>Initializes a new instance of the <see cref="KeyMaterialException"/> class.</summary>
    public KeyMaterialException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public KeyMaterialException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public KeyMaterialException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when a key version is used with an EBICS protocol version that does not permit it
/// (e.g. the PSS-based signature version <c>A006</c> with H003).
/// </summary>
public class KeyVersionNotPermittedException : EbicsCryptoException
{
    /// <summary>Initializes a new instance of the <see cref="KeyVersionNotPermittedException"/> class.</summary>
    public KeyVersionNotPermittedException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public KeyVersionNotPermittedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public KeyVersionNotPermittedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
