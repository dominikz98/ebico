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

## Branch-Protection für `main`

Die CI-Jobs oben sind erst dann ein echtes **Gate**, wenn GitHub den Merge blockiert,
solange sie nicht grün sind. Ohne Schutzregel ist ein roter PR mergebar und ein direkter
Push auf `main` möglich — die Definition of Done („CI grün") wäre allein durch Disziplin
abgesichert. Deshalb ist `main` per **Branch-Protection-Regel** geschützt (Issue #3,
[ADR-0028](../adr/0028-branch-protection-main.md)).

Die Regel lebt in den **Repo-Settings**, nicht im Repo-Inhalt — sie ist per Definition
nicht versionierbar. Dieser Abschnitt ist daher die maßgebliche Beschreibung des
Soll-Zustands; ein Guard-Test (`BranchProtectionDocTests`) hält wenigstens die Liste der
Required Checks mit `ci.yml` synchron.

### Required Status Checks

Genau die Jobs aus `ci.yml` — sie laufen auf jedem `pull_request`:

<!-- required-checks:start -->
- `Build & Test`
- `Docs Link Check`
- `Container Build (Server)`
- `Pack (NuGet, build-only)`
<!-- required-checks:end -->

Als Check-Kontext zählt der **Anzeigename** (`name:`) des Jobs, nicht der YAML-Schlüssel.
Wird ein Job umbenannt, hinzugefügt oder entfernt, muss die Liste hier **und** die
Repo-Einstellung nachgezogen werden.

> **Nicht** aufgenommen: der Job `Publish (NuGet + Container)` aus
> [`release.yml`](#release-workflow-releaseyml). Er feuert ausschließlich auf `v*.*.*`-Tags
> und würde als Required Check auf jedem PR ewig als „Expected — Waiting for status" hängen
> und den Merge dauerhaft blockieren.

### Weitere Einstellungen

| Einstellung | Wert | Warum |
| --- | --- | --- |
| `strict` (Branch muss aktuell sein) | **an** | Verhindert das semantische Merge-Loch: zwei PRs, je einzeln grün, können sich gegenseitig brechen. |
| Direkte Pushes auf `main` | **blockiert** | Änderungen laufen ausnahmslos über einen PR (siehe Workflow-Konvention „ein Issue → ein Branch → ein PR"). |
| `enforce_admins` | **an** | EBICO ist faktisch ein Solo-Repo; ohne Admin-Bindung wäre die Regel für den einzigen Committer wirkungslos. |
| Required approving reviews | **aus** | Ein Solo-Repo kann den eigenen PR nicht selbst approven — die Regel würde jeden Merge blockieren. Die Review-Pflicht bleibt als DoD-Punkt in der PR-Checkliste. |
| Force-Push / Branch löschen | **blockiert** | Historie von `main` bleibt linear und nachvollziehbar. |

Setzen bzw. prüfen lässt sich der Zustand über die API:

```bash
gh api repos/:owner/:repo/branches/main/protection            # Ist-Zustand
gh api repos/:owner/:repo/branches/main/protection --method PUT --input protection.json
```

**Bei rotem Gate:** Lässt ein defekter oder hängender Check keinen Merge mehr zu, ist der
Weg *nicht* der Force-Push, sondern die Regel in den Settings kurz zu deaktivieren, den
Fix zu mergen und sie sofort wieder zu aktivieren.

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
