namespace EBICO.Connector;

/// <summary>
/// Base type for errors raised by the EBICO connector. Business return codes are carried in
/// <see cref="EbicsResult{T}"/>; only genuine technical failures are thrown.
/// </summary>
public class EbicsConnectorException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EbicsConnectorException"/> class.</summary>
    public EbicsConnectorException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsConnectorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsConnectorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised for connector configuration problems: missing or invalid connection parameters, or a
/// request sent without a registered <see cref="IEbicsRequestHandler{TRequest, TResult}"/>.
/// </summary>
public sealed class EbicsConfigurationException : EbicsConnectorException
{
    /// <summary>Initializes a new instance of the <see cref="EbicsConfigurationException"/> class.</summary>
    public EbicsConfigurationException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised for transport-level failures: the server returned a non-success HTTP status, or the
/// HTTP request could not be completed (network error, timeout, cancellation of the transport).
/// </summary>
public sealed class EbicsTransportException : EbicsConnectorException
{
    /// <summary>Initializes a new instance of the <see cref="EbicsTransportException"/> class.</summary>
    public EbicsTransportException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsTransportException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
