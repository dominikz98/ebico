namespace EBICO.Server.Handlers;

/// <summary>
/// Raised by an order handler when the request's order data cannot be decoded, decrypted,
/// decompressed, deserialized or reconstructed, or carries an unusable/unpermitted key version.
/// The central error mapper maps this to <c>EBICS_INVALID_ORDER_DATA_FORMAT</c> (<c>090004</c>).
/// </summary>
/// <remarks>
/// Its dedicated type carries the "invalid order data" meaning regardless of the underlying cause,
/// so the mapping stays unambiguous — unlike the low-level exceptions it wraps
/// (<see cref="System.IO.InvalidDataException"/>, <see cref="System.FormatException"/>,
/// <see cref="System.Security.Cryptography.CryptographicException"/>, key/version exceptions, …),
/// which mean different things in other contexts.
/// </remarks>
public class EbicsOrderDataException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EbicsOrderDataException"/> class.</summary>
    public EbicsOrderDataException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsOrderDataException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause (e.g. a decryption or deserialization failure).</param>
    public EbicsOrderDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
