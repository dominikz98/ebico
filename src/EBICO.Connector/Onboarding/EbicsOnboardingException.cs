namespace EBICO.Connector.Onboarding;

/// <summary>
/// Raised when an onboarding flow fails a <b>security</b> or <b>integrity</b> check that is not a
/// business return code: a bank key whose fingerprint does not match the bank letter, a failed
/// certificate verification, or a malformed key-management response. These are technical failures
/// (thrown), as opposed to business return codes, which are surfaced via <see cref="EbicsResult{T}"/>.
/// </summary>
public sealed class EbicsOnboardingException : EbicsConnectorException
{
    /// <summary>Initializes a new instance of the <see cref="EbicsOnboardingException"/> class.</summary>
    public EbicsOnboardingException()
    {
    }

    /// <summary>Initializes a new instance with the given <paramref name="message"/>.</summary>
    /// <param name="message">The error message.</param>
    public EbicsOnboardingException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with the given message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EbicsOnboardingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
