using System.Globalization;
using System.Text;
using EBICO.Core.Crypto;

namespace EBICO.Connector.Onboarding.Letter;

/// <summary>
/// Builds the deterministic plain-text body of an initialization letter. Shared by the text and PDF
/// renderers so both show the same content.
/// </summary>
internal static class InitializationLetterTextBuilder
{
    /// <summary>Renders the letter text for <paramref name="model"/>.</summary>
    /// <param name="model">The letter data.</param>
    /// <returns>The letter as plain text (UTF-8 characters, <c>\n</c> line endings).</returns>
    public static string Build(InitializationLetterModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var title = model.Kind == LetterKind.Ini
            ? "EBICS INI-Brief (Initialisierung Signaturschlüssel)"
            : "EBICS HIA-Brief (Initialisierung Authentifikations- & Verschlüsselungsschlüssel)";

        var builder = new StringBuilder();
        builder.Append(title).Append('\n');
        builder.Append(new string('=', title.Length)).Append('\n').Append('\n');

        builder.Append("EBICS-Version: ").Append(model.VersionCode).Append('\n');
        builder.Append("HostID:        ").Append(model.HostId).Append('\n');
        builder.Append("PartnerID:     ").Append(model.PartnerId).Append('\n');
        builder.Append("UserID:        ").Append(model.UserId).Append('\n');
        builder.Append("Datum:         ")
            .Append(model.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" UTC").Append('\n').Append('\n');

        foreach (var key in model.Keys)
        {
            builder.Append(Label(key.Purpose)).Append(" (").Append(key.KeyVersion).Append(")\n");
            builder.Append("Hashwert (SHA-256):\n");
            foreach (var line in key.FingerprintText.Split('\n'))
            {
                builder.Append("    ").Append(line).Append('\n');
            }

            builder.Append('\n');
        }

        builder.Append("Ort, Datum: ______________________________\n\n");
        builder.Append("Unterschrift: ____________________________\n");
        return builder.ToString();
    }

    private static string Label(KeyPurpose purpose) => purpose switch
    {
        KeyPurpose.Signature => "Signaturschlüssel (A)",
        KeyPurpose.Authentication => "Authentifikationsschlüssel (X)",
        KeyPurpose.Encryption => "Verschlüsselungsschlüssel (E)",
        _ => purpose.ToString(),
    };
}
