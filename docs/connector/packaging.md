# Connector: NuGet-Packaging & Beispiele

> Umsetzung von **Issue #50** (Milestone M6 — Connector, Abschluss). Diese Seite beschreibt, wie die
> beiden veröffentlichten Bibliotheken **`EBICO.Core`** und **`EBICO.Connector`** als NuGet-Pakete
> gebaut werden: die Paket-Metadaten, die **Symbols/SourceLink**-Einbindung, die Paket-READMEs, die
> **CalVer**-Versionierung und der lauffähige **Quickstart-Sample**. Grundlage sind der
> [Client-Kern](client-core.md) (#46) und die [Connector-Architektur](architecture.md). Der eigentliche
> **Publish/Push** in einen Feed ist bewusst auf **M9 / #62** verschoben; #50 legt das Fundament und
> validiert die Packbarkeit in der CI. Grundsatzentscheidung:
> [ADR-0024](../adr/0024-nuget-packaging-und-versionierung.md).

## Zweck

`EBICO.Connector` ist als NuGet-Client gedacht (wie *Azurite* das Gegenstück auf der Server-Seite ist).
Damit er auslieferbar ist, brauchen die Bibliotheken vollständige Paket-Metadaten, Debug-Symbole und
eine reproduzierbare Versionierung. Da der Connector `EBICO.Core` per `ProjectReference` nutzt, muss
Core **ebenfalls** ein Paket sein — sonst hätte das Connector-Paket eine unauflösbare Abhängigkeit.

Zentraler Ort für die gemeinsamen Felder ist [`Directory.Build.props`](../../Directory.Build.props),
konditioniert auf die beiden Bibliotheks-Projekte (gleiches Muster wie die bereits vorhandene
`GenerateDocumentationFile`-Regel). Projekt-spezifische Felder (`Description`, `PackageTags`, die
Paket-`README.md`) stehen in der jeweiligen `.csproj`.

## Zwei Pakete (Core + Connector)

| Paket | Inhalt | Abhängigkeit |
| --- | --- | --- |
| `EBICO.Core` | Geteilte Primitives (Schema/Serialisierung, Krypto, BTF/Order-Modelle, Returncodes) | — |
| `EBICO.Connector` | Client-Pipeline (Mediator-Muster), Onboarding/Upload/Download-API | `EBICO.Core` (gleiche Version) + `Microsoft.Extensions.*` |

Beim `dotnet pack` des Connectors wird `EBICO.Core` **nicht** eingebettet, sondern als
Paket-Abhängigkeit in die `.nuspec` geschrieben (mit exakt gleicher Version). Ein Konsument zieht also
beide Pakete. Die schlanke Fremd-Abhängigkeitsliste (nur `Microsoft.Extensions.*` und — für den
INI-/HIA-Brief — QuestPDF) bleibt damit erhalten.

## Paket-Metadaten

Gesetzt in [`Directory.Build.props`](../../Directory.Build.props) (gemeinsam) bzw. in den `.csproj`
(projekt-spezifisch):

| Feld | Wert |
| --- | --- |
| `PackageId` | Projektname (`EBICO.Core`, `EBICO.Connector`) |
| `Authors` / `Company` | `Dominik Zettl` / `tecvia` |
| `Description` / `PackageTags` | je Projekt in der `.csproj` |
| `PackageLicenseExpression` | `MIT` (siehe [`LICENSE`](../../LICENSE)) |
| `PackageProjectUrl` / `RepositoryUrl` | `https://github.com/dominikz98/ebico` |
| `PackageReadmeFile` | `README.md` (je Projekt, mit ins Paket gepackt) |
| `IncludeSymbols` / `SymbolPackageFormat` | `true` / `snupkg` |

Die XML-Doku (`GenerateDocumentationFile`, bereits für Core+Connector aktiv) landet automatisch als
`lib/net10.0/<Assembly>.xml` im Paket.

## Versionierung (CalVer)

Die Version folgt dem Schema **`{JAHR}.{MONAT}.{BUILD}`** (Kalenderversionierung, bewusst **statt**
SemVer — siehe [ADR-0024](../adr/0024-nuget-packaging-und-versionierung.md)):

```
VersionPrefix = <UTC-Jahr>.<UTC-Monat>.$(EbicoBuildNumber)
```

- **BUILD** kommt in der CI aus `github.run_number` (`-p:EbicoBuildNumber=…`, monoton steigend), lokal
  Default `0` (→ z. B. `2026.7.0`).
- **Normalisierung:** NuGet/MSBuild behandeln Versionskomponenten als Integer — die führende Null im
  Monat entfällt (`2026.07.1` → **`2026.7.1`**). Das ist erwartet und ändert die Ordnung nicht.
- Über SourceLink trägt `AssemblyInformationalVersion` zusätzlich den Commit-SHA (`2026.7.1+<sha>`).

CalVer kodiert **keine** API-Kompatibilität; Breaking Changes werden über Release-Notes/Changelog
kommuniziert, nicht über die Versionsnummer (Trade-off in ADR-0024).

## Symbols & SourceLink

`Microsoft.SourceLink.GitHub` (build-only, `PrivateAssets=all`) bettet die Repository-/Commit-Info ein;
`IncludeSymbols=true` + `SymbolPackageFormat=snupkg` erzeugt neben jedem `.nupkg` ein `.snupkg` mit dem
`.pdb`. Zusammen mit `PublishRepositoryUrl`/`EmbedUntrackedSources` erlaubt das Step-Debugging bis in die
Quellen des jeweiligen Commits. `ContinuousIntegrationBuild` wird nur in der CI (`GITHUB_ACTIONS`) gesetzt
(deterministische Pfade).

## Paket-README

Jedes Paket bringt eine eigene `README.md` mit (`src/EBICO.Core/README.md`,
`src/EBICO.Connector/README.md`), die auf nuget.org als Paketbeschreibung gerendert wird. Sie verlinken
mit **absoluten** GitHub-URLs (relative Repo-Links würden auf nuget.org nicht auflösen).

## Quickstart-Sample

[`samples/EBICO.Connector.Quickstart`](../../samples/EBICO.Connector.Quickstart/README.md) ist eine
**selbstständige Konsolenapp**: sie startet den `EBICO.Server`-Emulator **in-process** (Kestrel,
ephemerer Loopback-Port), seedet die Stammdaten und fährt mit dem Connector den vollständigen Rundlauf.
Kein externer Server, keine echte Bank:

```bash
dotnet run --project samples/EBICO.Connector.Quickstart
```

Der Ablauf (in `QuickstartRunner.RunAsync`, auch aus Tests aufrufbar):

1. Teilnehmerschlüssel erzeugen (`ISubscriberKeyGenerator.GenerateAsync`, A00x/X002/E002),
2. Onboarding **INI → HIA → HPB** (Bank-Fingerprints in-flow geprüft),
3. Upload **CCT** (`pain.001`, selbst erzeugte, nicht-proprietäre Sample-Daten in `SamplePain`),
4. Download **C53** (`camt.053`) mit Parse-Hook (ZIP-Einträge auslesen).

Ein *echter* Einsatz zeigt statt des in-process-Servers auf die Bank-URL bzw. auf einen separat
gestarteten `EBICO.Server`; DI-Setup und `IEbicsClient.Send` bleiben identisch.

## Tests

`tests/EBICO.Tests/Packaging/` sichert das Feature ab:

- **`PackageMetadataTests`** — prüft reflektiv für die `EBICO.Core`- und `EBICO.Connector`-Assemblies,
  dass die `AssemblyInformationalVersion` dem CalVer-Muster entspricht und
  `Description`/`Company`/`Copyright` gesetzt sind.
- **`QuickstartSampleTests`** — Smoke-Test: führt `QuickstartRunner.RunAsync` aus und belegt den
  vollständigen Rundlauf (INI/HIA/HPB `000000`, CCT `000000`, C53 **`011000`**).

Die tatsächlichen **Paketinhalte** (README, XML-Doc, Lizenz-Expression, Core-Dependency, `.snupkg`)
werden vom CI-`pack`-Job validiert — ein fehlerhaftes README-Wiring bräche `dotnet pack` z. B. mit
`NU5039`.

## CI / Publish

Der [`pack`-Job](../development/ci.md) baut bei jedem Push/PR nach `build-test` beide Pakete
(`*.nupkg` + `*.snupkg`) nach `./artifacts` und lädt sie als Artefakt hoch — **build-only, kein
Registry-Push** (Regressionsschutz, analog zum `container-build`-Job).

Der authentifizierte **Push nach nuget.org** erfolgt seit **M9 / #62** in der tag-getriggerten
[Release-Pipeline](../development/release.md) (`.github/workflows/release.yml`,
[ADR-0027](../adr/0027-nuget-publish-und-release-pipeline.md)): Ein Tag `vJAHR.MONAT.N` leitet die
Version ab, packt Core + Connector mit dieser Version und pusht sie (inkl. `.snupkg`-Symbole) nach
nuget.org (Secret `NUGET_API_KEY`, `--skip-duplicate`); zusätzlich entsteht ein GitHub-Release mit
auto-generierten Notes. Der bloße Merge publiziert nichts — der Push feuert nur beim Tag.

## Offene Punkte

- `Authors`/`Company` sind Platzhalter; bei einem offiziellen Release ggf. auf die endgültige
  Herausgeber-/Firmenbezeichnung anpassen.

## Verwandte Doku

- [Connector-Architektur](architecture.md) — Gesamtentwurf, Send-Pipeline
- [Client-Kern & Konfiguration](client-core.md) — #46: `AddEbicoConnector`, Options/DI
- [Onboarding](onboarding.md) · [Upload](upload.md) · [Download](download.md) — die im Sample genutzten Flows
- [CI-Pipeline](../development/ci.md) — der `pack`-Job (build-only)
- [Release-Runbook](../development/release.md) — Tag setzen → nuget.org-/GHCR-Push (#62)
- [ADR-0024 — NuGet-Packaging & Versionierung](../adr/0024-nuget-packaging-und-versionierung.md)
- [ADR-0027 — NuGet-Publish- & Release-Pipeline](../adr/0027-nuget-publish-und-release-pipeline.md)
- [Lizenz & Repo-Policy](../legal/ebics-licensing.md) — proprietäre EBICS-Schemas (nicht Teil der Pakete)

---

> Diese Seite ist die gepflegte Referenz. Bei Änderungen am Packaging hier (und im
> [Doku-Index](../index.md)) nachziehen.
