namespace EBICO.Connector.Onboarding.Letter;

/// <summary>
/// Renders an <see cref="InitializationLetterModel"/> into an <see cref="InitializationLetter"/>.
/// The default text renderer produces the plain-text form only; the QuestPDF renderer additionally
/// produces the PDF. Which one is active depends on the DI registration.
/// </summary>
public interface IInitializationLetterRenderer
{
    /// <summary>Renders the letter.</summary>
    /// <param name="model">The letter data.</param>
    /// <returns>The rendered letter (text, and PDF when supported).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    InitializationLetter Render(InitializationLetterModel model);
}
