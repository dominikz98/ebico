namespace EBICO.Connector.Onboarding;

/// <summary>
/// A rendered EBICS initialization letter (INI or HIA): the plain-text form (always present) and,
/// when a PDF renderer is registered, the same letter as a PDF document. The letter lists the
/// subscriber's key fingerprints for the manual comparison the bank performs before activating the
/// subscriber.
/// </summary>
public sealed class InitializationLetter
{
    /// <summary>The plain-text rendering of the letter (UTF-8).</summary>
    public required string Text { get; init; }

    /// <summary>The PDF rendering of the letter, or <see langword="null"/> when no PDF renderer is registered.</summary>
    public byte[]? Pdf { get; init; }
}
