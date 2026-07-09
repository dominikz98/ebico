using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EBICO.Connector.Onboarding.Letter;

/// <summary>
/// Renders the initialization letter as an A4 PDF with QuestPDF, in addition to the plain text. The
/// content is the same monospaced body produced by <see cref="InitializationLetterTextBuilder"/> so
/// the SHA-256 fingerprint groups stay aligned.
/// </summary>
/// <remarks>
/// Uses the QuestPDF <b>Community</b> license (set once in the static constructor), which is free for
/// organisations below the revenue threshold — see <c>docs/adr/0010-pdf-bibliothek.md</c>.
/// </remarks>
internal sealed class PdfInitializationLetterRenderer : IInitializationLetterRenderer
{
    static PdfInitializationLetterRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public InitializationLetter Render(InitializationLetterModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var text = InitializationLetterTextBuilder.Build(model);
        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontFamily(Fonts.CourierNew).FontSize(10));
                page.Content().Text(text);
            });
        }).GeneratePdf();

        return new InitializationLetter { Text = text, Pdf = pdf };
    }
}
