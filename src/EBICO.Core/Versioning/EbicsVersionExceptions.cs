namespace EBICO.Core.Versioning;

/// <summary>
/// Base type for errors raised while detecting or dispatching an EBICS protocol
/// version.
/// </summary>
public class EbicsVersionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EbicsVersionException"/> class.</summary>
    public EbicsVersionException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsVersionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an envelope's root namespace does not belong to any supported EBICS
/// version (H003/H004/H005).
/// </summary>
public class EbicsVersionNotSupportedException : EbicsVersionException
{
    /// <summary>Initializes a new instance of the <see cref="EbicsVersionNotSupportedException"/> class.</summary>
    public EbicsVersionNotSupportedException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsVersionNotSupportedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsVersionNotSupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an envelope is not well-formed enough to determine its version (not
/// XML, empty, truncated, or missing a root element), its root element is not a recognized EBICS
/// envelope, or it is well-formed but cannot be mapped onto the version's binding (a schema-invalid
/// document — see <see cref="Serialization.EbicsXmlSerializer.DeserializeEnvelope"/>).
/// </summary>
/// <remarks>
/// All of these are faults of the <em>inbound</em> document, so the server maps this exception to
/// <c>091010 EBICS_INVALID_XML</c> rather than to an internal error (issue #117).
/// </remarks>
public class EbicsEnvelopeFormatException : EbicsVersionException
{
    /// <summary>Initializes a new instance of the <see cref="EbicsEnvelopeFormatException"/> class.</summary>
    public EbicsEnvelopeFormatException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsEnvelopeFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsEnvelopeFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised by the strict detection path when an envelope's root namespace and its
/// <c>@Version</c> attribute disagree (e.g. an <c>urn:org:ebics:H005</c> root that
/// declares <c>Version="H004"</c>).
/// </summary>
public class EbicsVersionMismatchException : EbicsVersionException
{
    /// <summary>Initializes a new instance of the <see cref="EbicsVersionMismatchException"/> class.</summary>
    public EbicsVersionMismatchException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsVersionMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
