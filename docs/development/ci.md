# CI-Pipeline (GitHub Actions)

Beschreibt den Continuous-Integration-Workflow (`.github/workflows/ci.yml`).
Gehört zu Issue **#7 — CI-Pipeline (GitHub Actions)** (Milestone M0).

## Trigger

- `pull_request` — jeder PR wird gebaut und getestet (Gate vor dem Merge).
- `push` auf `main` — Validierung des Hauptzweigs nach dem Merge.

Eine `concurrency`-Gruppe bricht ältere, noch laufende Läufe desselben
Branches/PRs ab, sobald ein neuer Commit kommt.

## Jobs

### `build-test`

1. **Checkout** (`actions/checkout`).
2. **Setup .NET** (`actions/setup-dotnet`) — installiert das SDK aus
   [`global.json`](../../global.json) (SDK-Version-Pinning).
3. **NuGet-Cache** (`actions/cache`) — Cache von `~/.nuget/packages`, Key über
   `hashFiles('**/*.csproj', 'Directory.Packages.props')`. Ändern sich
   Abhängigkeiten, ändert sich der Key.
4. **Restore** (`dotnet restore`).
5. **Build** (`-c Release --no-restore`) — `TreatWarningsAsErrors=true` aus
   `Directory.Build.props` macht jede neue Warnung zum Fehler (DoD).
6. **Test** (`--no-build`) — Coverage via `--collect:"XPlat Code Coverage"`
   (coverlet) + TRX-Logger.
7. **Artefakte**: `coverage.cobertura.xml` (Coverage) und `*.trx` (Testbericht)
   werden hochgeladen — auch bei rotem Lauf (`if: always()`).

### `docs-link-check`

Prüft mit [lychee](https://github.com/lycheeverse/lychee-action) **relative**
Doku-Links (Docs-as-Code). Läuft bewusst **offline** (`--offline`): externe URLs
(z. B. die vielen `ebics.org`-Links in `docs/protocol/schema-sources.md`) werden
nicht geprüft, um flakige Netz-Requests zu vermeiden. Tote relative Links (etwa
nach dem Verschieben einer Doku-Seite) lassen den Job fehlschlagen.

### `pack`

Packt (nach `build-test`) die veröffentlichten Bibliotheken **`EBICO.Core`** und
**`EBICO.Connector`** in Release nach `./artifacts` und lädt `*.nupkg` + `*.snupkg`
als Artefakt `nuget` hoch (Issue #50). Der Job validiert die echte **Packbarkeit**
(README, XML-Doc, Symbole, SourceLink, Lizenz-Expression) — ein fehlendes
Paket-README bräche ihn z. B. mit `NU5039`. Die CalVer-BUILD-Komponente kommt aus
`github.run_number` (`-p:EbicoBuildNumber=…`), siehe
[packaging.md](../connector/packaging.md) und
[ADR-0024](../adr/0024-nuget-packaging-und-versionierung.md). **Build-only:** kein
Registry-Push — das gehört zur Publish-Pipeline (M9 / #62), analog zum
`container-build`-Job.

## Release-Workflow (`release.yml`)

Der Push/Publish läuft **getrennt** von der CI in `.github/workflows/release.yml` (M9 / #62,
[ADR-0027](../adr/0027-nuget-publish-und-release-pipeline.md)). Trigger ist **nicht** `main`/PR, sondern
das Pushen eines **Tags `v*.*.*`** — die CI-Jobs oben bleiben davon unberührt. Der Job `release`:

1. **Version aus dem Tag** ableiten und gegen das CalVer-Muster prüfen (`v2026.7.42` → `2026.7.42`).
2. **Build + Test** in Release mit `-p:Version=<version>` (überschreibt die datumsbasierte CalVer-Zahl;
   re-verifiziert die DoD).
3. **Pack** `EBICO.Core` + `EBICO.Connector` mit derselben Version → `./artifacts`.
4. **Push nach nuget.org** (`dotnet nuget push`, Secret `NUGET_API_KEY`, `--skip-duplicate`; `.snupkg`
   automatisch mit).
5. **GHCR-Container-Push** `ghcr.io/dominikz98/ebico-server:{VERSION}` + `:latest` (via `GITHUB_TOKEN`).
6. **GitHub-Release** mit auto-generierten Notes und den NuGet-Artefakten (`gh release create --generate-notes`).

Der Workflow ist **inert**, bis Maintainer das Secret `NUGET_API_KEY` setzen und einen Tag pushen — der
bloße Merge publiziert nichts. Schritt-für-Schritt: [Release-Runbook](release.md).

## Reproduzierbarkeit ohne Lock-Files

Es werden **keine** `packages.lock.json` verwendet. Reproduzierbare Restores
kommen aus der zentralen Paketverwaltung: alle Versionen sind in
`Directory.Packages.props` exakt gepinnt, inkl. transitiver Pakete
(`CentralPackageTransitivePinningEnabled=true`). Hintergrund: Das implizite
Blazor-Asset-Paket `Microsoft.AspNetCore.App.Internal.Assets` hängt am
SDK-Runtime-Patch, wodurch eingecheckte Lock-Files `--locked-mode` zwischen
Maschinen mit unterschiedlichem SDK-Patch brechen (NU1004). Details:
[solution-layout.md](solution-layout.md).

## Später

- **Externer Link-Check** als nicht-blockierender `schedule`-Job (nächtlich).

> **Erledigt (M9 / #62):** Der authentifizierte **Publish/Push** ist seit #62 im
> [Release-Workflow](#release-workflow-releaseyml) umgesetzt (nuget.org + GHCR, tag-getrieben,
> [ADR-0027](../adr/0027-nuget-publish-und-release-pipeline.md)). Der build-only `pack`-Job in `ci.yml`
> bleibt als Regressionsschutz erhalten.
