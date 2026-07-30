using EBICO.Connector.Quickstart;
using EBICO.Core;

// Minimal EBICS quickstart: starts the EBICO.Server emulator in-process and drives the full
// round-trip onboarding -> upload -> download with the EBICO.Connector. No external
// server and no real bank needed — just `dotnet run`.
//
// The EBICS version is selectable (default H005):
//   dotnet run --project samples/EBICO.Connector.Quickstart -- --version H004
//   dotnet run --project samples/EBICO.Connector.Quickstart -- H003
//   EBICO_QUICKSTART_VERSION=H004 dotnet run --project samples/EBICO.Connector.Quickstart
var version = ResolveVersion(args, Environment.GetEnvironmentVariable("EBICO_QUICKSTART_VERSION"));

var result = await QuickstartRunner.RunAsync(Console.Out, version);

// Exit code 0 only when every step succeeded functionally (handy for CI/scripts).
return result.Success ? 0 : 1;

// Resolves the EBICS version from the arguments (`--version <v>`, `--version=<v>` or positional `<v>`)
// or from the environment variable; falls back to H005 when the value is missing/invalid.
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
