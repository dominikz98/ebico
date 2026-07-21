# 0027 — NuGet-Publish- & Release-Pipeline (nuget.org, tag-getrieben)

- Status: accepted
- Datum: 2026-07-21

## Kontext

M9 (#62) verlangt eine **Publish-Pipeline**: „Pack + Push in CI (nuget.org / GitHub Packages)",
„Versionierung/Tags" und „Release-Notes-Automatisierung". Ausgangslage nach #50/#61: Die beiden
Bibliotheken `EBICO.Core` + `EBICO.Connector` haben vollständige Paketmetadaten und CalVer-Versionierung
([ADR-0024](0024-nuget-packaging-und-versionierung.md)), und die CI hat einen **build-only** `pack`-Job
sowie einen build-only `container-build`-Job — beide bewusst **ohne** Registry-Push, der explizit auf
diese Pipeline verschoben wurde ([ADR-0022](0022-container-image-und-konfiguration.md)).

Offen und hier zu entscheiden waren: das **Publish-Ziel** (nuget.org vs. GitHub Packages), der
**Release-Trigger** und die **Versionsherkunft** beim Release (ADR-0024 leitet BUILD aus
`github.run_number` ab — für einen kontrollierten Release ist das nicht reproduzierbar/vorhersagbar),
sowie die **Release-Notes**-Strategie. Der Container-Push nach ADR-0022 hat hier ebenfalls seinen Platz.

## Entscheidung

1. **Publish-Ziel: nuget.org.** Die Pakete werden nach `https://api.nuget.org/v3/index.json` gepusht
   (Ziel: breite, auth-freie Konsumierbarkeit — der Connector ist als öffentlicher Client gedacht, wie
   *Azurite* das Server-Gegenstück ist). Der API-Key liegt als Repo-Secret **`NUGET_API_KEY`**.
   Die `.snupkg`-Symbolpakete werden von `dotnet nuget push` automatisch mit publiziert;
   `--skip-duplicate` macht Wiederholungen idempotent.
2. **Tag-getrieben mit Version-aus-Tag.** Ein eigener Workflow **`.github/workflows/release.yml`** feuert
   auf `push` von Tags `v*.*.*`. Die Version wird aus dem Tag abgeleitet (`v2026.7.42` → `2026.7.42`) und
   per `-p:Version=` über die datumsbasierte CalVer-Berechnung aus `Directory.Build.props` gelegt. Der
   Tag muss dem CalVer-Muster `{JAHR}.{MONAT}.{BUILD}` entsprechen (Guard im Workflow). Das **verfeinert**
   ADR-0024 (BUILD aus `run_number`) für **Release**-Builds: der Tag ist die Quelle der Wahrheit; die
   run-number-basierte Version bleibt für die build-only `pack`-Regressionsprüfung in `ci.yml`.
3. **Release-Notes automatisch.** Der Workflow erstellt ein GitHub-Release
   (`gh release create --generate-notes`) mit aus den PRs/Commits seit dem letzten Tag generierten Notes
   und hängt die `.nupkg`/`.snupkg` an. **Kein** handgepflegtes `CHANGELOG.md` — die GitHub-Releases sind
   der Changelog (löst den in ADR-0024 genannten „Release-Notes/Changelog"-Kanal ein).
4. **Container-Push nach GHCR.** Derselbe Workflow baut das Server-Image (`--build-arg PROJECT=EBICO.Server`,
   wie der `container-build`-Job) und pusht `ghcr.io/dominikz98/ebico-server:{VERSION}` **und** `:latest`
   nach GHCR — authentifiziert über das automatische `GITHUB_TOKEN` (`permissions: packages: write`), ohne
   externes Secret. Das erfüllt den in ADR-0022 auf „die Publish-Pipeline #62" verschobenen Push.
5. **Getrennter Workflow.** `release.yml` ist von `ci.yml` getrennt, weil der Tag-Trigger sich vom
   CI-Trigger (`main`/PR) unterscheidet; `ci.yml` bleibt für Build/Test/Pack-Regression zuständig.

## Konsequenzen

- Ein Release entsteht durch **Setzen und Pushen eines Tags** `vJAHR.MONAT.N`; alles Weitere (Build/Test,
  Pack, nuget.org-Push, GHCR-Image, GitHub-Release) läuft automatisch. Runbook:
  [../development/release.md](../development/release.md).
- **Inert bis konfiguriert:** Ohne `NUGET_API_KEY` schlägt der NuGet-Push fehl; der Merge des Workflows
  selbst publiziert nichts. Das Tag-Gate schützt vor versehentlicher Veröffentlichung — relevant, weil
  nuget.org-Pushes praktisch unwiderruflich sind (nur „unlisten").
- Paket- und Assembly-Version sind deckungsgleich (dasselbe `-p:Version` bei Build und Pack); die
  bestehenden `PackageMetadataTests` (CalVer-Muster) bleiben gültig.
- **Trade-off GitHub-only Release-Notes:** wer den Changelog offline/im Repo will, findet ihn nicht als
  Datei; dafür entfällt der Pflegeaufwand und Doppelspurigkeit.

## Alternativen

- **GitHub Packages** (statt/zusätzlich nuget.org): nutzt `GITHUB_TOKEN` ohne externes Konto, aber
  Installation nur mit GitHub-Authentifizierung — verworfen zugunsten der breiten, auth-freien
  nuget.org-Konsumierbarkeit (Projektziel).
- **GitHub-Release-Event** (`on: release: [published]`) als Trigger: erfordert das manuelle Anlegen eines
  Releases in der UI; verworfen zugunsten des rein tag-getriebenen, vollautomatischen Flows (Release wird
  vom Workflow erzeugt).
- **Publish bei jedem main-Push** (CalVer aus `run_number`): kontinuierlich ohne Tags, aber ohne
  kontrollierten Release-Zeitpunkt und entgegen der #62-Anforderung „Versionierung/Tags" — verworfen.
- **Handgepflegtes `CHANGELOG.md`:** volle Kontrolle über die Notes, aber Pflegeaufwand und Redundanz zu
  den GitHub-Releases — verworfen zugunsten der Automatisierung.
