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
- **Pack + Publish** des Connector-NuGet-Pakets (M9 / #62) — im Workflow bereits
  als auskommentierter `pack-connector`-Job vorbereitet.
