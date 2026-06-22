namespace EBICO.Core.Domain;

/// <summary>
/// Base type for validation errors raised by the EBICO domain model (identifiers,
/// subscriber lifecycle, permissions).
/// </summary>
public class EbicsDomainException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EbicsDomainException"/> class.</summary>
    public EbicsDomainException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsDomainException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsDomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when a string is not a valid EBICS identifier (<c>HostID</c>, <c>PartnerID</c>,
/// <c>UserID</c> or <c>SystemID</c>) — null, empty, too long, or containing characters
/// outside <c>[a-zA-Z0-9,=]</c>.
/// </summary>
public class InvalidEbicsIdentifierException : EbicsDomainException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidEbicsIdentifierException"/> class.</summary>
    public InvalidEbicsIdentifierException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public InvalidEbicsIdentifierException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public InvalidEbicsIdentifierException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an illegal <see cref="SubscriberState"/> transition is attempted via
/// <see cref="Subscriber.Transition(SubscriberState)"/>.
/// </summary>
public class InvalidSubscriberStateTransitionException : EbicsDomainException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidSubscriberStateTransitionException"/> class.</summary>
    public InvalidSubscriberStateTransitionException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public InvalidSubscriberStateTransitionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public InvalidSubscriberStateTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
