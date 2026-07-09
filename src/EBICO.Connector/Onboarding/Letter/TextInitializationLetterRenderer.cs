namespace EBICO.Connector.Onboarding.Letter;

/// <summary>
/// The dependency-free default renderer: produces the plain-text letter only
/// (<see cref="InitializationLetter.Pdf"/> is <see langword="null"/>).
/// </summary>
internal sealed class TextInitializationLetterRenderer : IInitializationLetterRenderer
{
    /// <inheritdoc />
    public InitializationLetter Render(InitializationLetterModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new InitializationLetter { Text = InitializationLetterTextBuilder.Build(model) };
    }
}
