using EBICO.Connector.Quickstart;
using EBICO.Core;

// Minimaler EBICS-Quickstart: startet den EBICO.Server-Emulator in-process und fährt mit dem
// EBICO.Connector den vollständigen Rundlauf Onboarding -> Upload -> Download. Kein externer
// Server, keine echte Bank nötig — einfach `dotnet run`.
//
// Die EBICS-Version ist wählbar (Default H005):
//   dotnet run --project samples/EBICO.Connector.Quickstart -- --version H004
//   dotnet run --project samples/EBICO.Connector.Quickstart -- H003
//   EBICO_QUICKSTART_VERSION=H004 dotnet run --project samples/EBICO.Connector.Quickstart
var version = ResolveVersion(args, Environment.GetEnvironmentVariable("EBICO_QUICKSTART_VERSION"));

var result = await QuickstartRunner.RunAsync(Console.Out, version);

// Exit-Code 0 nur, wenn jeder Schritt fachlich erfolgreich war (für CI/Skripte).
return result.Success ? 0 : 1;

// Löst die EBICS-Version aus den Argumenten (`--version <v>`, `--version=<v>` oder positional `<v>`)
// bzw. der Umgebungsvariablen auf; fällt bei fehlender/ungültiger Angabe auf H005 zurück.
static EbicsVersion ResolveVersion(string[] args, string? envValue)
{
    var candidates = new List<string?>();
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            candidates.Add(args[i + 1]);
        }
        else if (args[i].StartsWith("--version=", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(args[i]["--version=".Length..]);
        }
        else if (!args[i].StartsWith('-'))
        {
            candidates.Add(args[i]);
        }
    }

    candidates.Add(envValue);

    foreach (var candidate in candidates)
    {
        if (!string.IsNullOrWhiteSpace(candidate)
            && Enum.TryParse<EbicsVersion>(candidate.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }
    }

    return EbicsVersion.H005;
}
