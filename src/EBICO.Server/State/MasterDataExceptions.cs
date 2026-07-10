namespace EBICO.Server.State;

/// <summary>
/// Base type for errors raised by the <see cref="IMasterDataManager"/> while enforcing the
/// master-data invariants (referential integrity, existence of referenced aggregates).
/// </summary>
public class MasterDataException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MasterDataException"/> class.</summary>
    public MasterDataException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public MasterDataException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public MasterDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an operation references a bank (<c>HostID</c>) that is not registered — for
/// example, creating a partner or subscriber for an unknown host.
/// </summary>
public class UnknownBankException : MasterDataException
{
    /// <summary>Initializes a new instance of the <see cref="UnknownBankException"/> class.</summary>
    public UnknownBankException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public UnknownBankException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public UnknownBankException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an operation references a partner (<c>PartnerID</c>) that is not registered at
/// the given bank — for example, creating a subscriber for an unknown partner.
/// </summary>
public class UnknownPartnerException : MasterDataException
{
    /// <summary>Initializes a new instance of the <see cref="UnknownPartnerException"/> class.</summary>
    public UnknownPartnerException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public UnknownPartnerException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public UnknownPartnerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when an operation targets a subscriber (the (<c>HostID</c>, <c>PartnerID</c>,
/// <c>UserID</c>) triple) that is not registered — for example, a state transition or
/// permission change on an unknown subscriber.
/// </summary>
public class UnknownSubscriberException : MasterDataException
{
    /// <summary>Initializes a new instance of the <see cref="UnknownSubscriberException"/> class.</summary>
    public UnknownSubscriberException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public UnknownSubscriberException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public UnknownSubscriberException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
