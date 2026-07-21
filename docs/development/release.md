# Release-Runbook (Publish nach nuget.org & GHCR)

Wie ein Release von EBICO geschnitten wird. Umsetzung von **Issue #62** (Milestone M9 — Packaging &
Docs). Grundsatzentscheidung: [ADR-0027](../adr/0027-nuget-publish-und-release-pipeline.md). Der
Workflow ist [`.github/workflows/release.yml`](../../.github/workflows/release.yml).

## Kurzfassung

Ein Release entsteht durch **Setzen und Pushen eines Tags** `vJAHR.MONAT.N`. Alles Weitere läuft
automatisch:

```bash
git tag v2026.7.1      # CalVer: {JAHR}.{MONAT}.{BUILD}
git push origin v2026.7.1
```

Der Tag-Push triggert `release.yml`; ein normaler `main`-Push oder PR löst **kein** Release aus
(der läuft weiter über [`ci.yml`](ci.md)).

## Voraussetzungen (einmalig durch Maintainer)

- **Secret `NUGET_API_KEY`** — ein API-Key von nuget.org mit Push-Recht für `EBICO.*`, hinterlegt unter
  *Repo → Settings → Secrets and variables → Actions*. **Ohne** dieses Secret schlägt der NuGet-Push
  fehl; nichts wird veröffentlicht.
- **GHCR** braucht **kein** zusätzliches Secret — der Container-Push nutzt das automatische
  `GITHUB_TOKEN` (`permissions: packages: write` im Workflow).

## Versionsschema

Die Version folgt **CalVer `{JAHR}.{MONAT}.{BUILD}`** ([ADR-0024](../adr/0024-nuget-packaging-und-versionierung.md)).
Beim Release ist der **Tag die Quelle der Wahrheit**: `v2026.7.42` → Paket-/Image-Version `2026.7.42`
(das `v`-Präfix wird entfernt). Der Workflow bricht ab, wenn der Tag nicht dem Muster
`v<zahl>.<zahl>.<zahl>` entspricht. NuGet normalisiert führende Nullen (`v2026.07.1` → `2026.7.1`).

> Abgrenzung zum `pack`-Job in `ci.yml`: dort kommt die BUILD-Komponente aus `github.run_number` (reiner
> Regressions-Pack, kein Push). Für **Releases** überschreibt die Tag-Version das per `-p:Version=`.

## Was der Workflow tut (`release.yml`)

1. **Version aus dem Tag ableiten** und gegen das CalVer-Muster prüfen.
2. **Restore → Build → Test** in Release (mit der Tag-Version; `TreatWarningsAsErrors` gilt weiter).
3. **Pack** `EBICO.Core` + `EBICO.Connector` (`*.nupkg` + `*.snupkg`) mit der Tag-Version → `./artifacts`.
4. **Push nach nuget.org** (`dotnet nuget push`, `--skip-duplicate`; `.snupkg`-Symbole werden automatisch
   mit publiziert).
5. **GHCR-Container-Push** `ghcr.io/dominikz98/ebico-server:{VERSION}` **und** `:latest`.
6. **GitHub-Release** mit auto-generierten Release-Notes (aus den PRs/Commits seit dem letzten Tag) und
   den NuGet-Artefakten als Anhang.

## Nach dem Release prüfen

- **nuget.org:** `EBICO.Core` und `EBICO.Connector` in der Ziel-Version gelistet (Indizierung kann einige
  Minuten dauern). nuget.org-Pushes sind praktisch **unwiderruflich** (nur „unlisten").
- **GHCR:** `docker pull ghcr.io/dominikz98/ebico-server:<version>` funktioniert.
- **GitHub-Release:** unter *Releases* mit generierten Notes und angehängten `*.nupkg`/`*.snupkg`.

## Fehlersuche

| Symptom | Ursache / Abhilfe |
| --- | --- |
| Workflow bricht bei „Version aus Tag ableiten" ab | Tag entspricht nicht `vJAHR.MONAT.BUILD` (nur Ziffern, drei Komponenten). |
| NuGet-Push schlägt fehl (401/403) | `NUGET_API_KEY` fehlt/abgelaufen oder ohne Push-Recht für `EBICO.*`. |
| Paket „already exists" | Version wurde bereits gepusht; `--skip-duplicate` überspringt sie (kein Fehler). |
| GHCR-Push „denied" | `packages: write`-Permission fehlt bzw. Package-Sichtbarkeit/Verknüpfung prüfen. |

## Verwandte Doku

- [CI-Pipeline](ci.md) — Build/Test/Pack (build-only) je Push/PR
- [Packaging & Beispiele (NuGet)](../connector/packaging.md) — Paketmetadaten, Symbols, CalVer
- [Container-Image](../deployment/container.md) — Image-Build & GHCR-Push
- [ADR-0027 — NuGet-Publish- & Release-Pipeline](../adr/0027-nuget-publish-und-release-pipeline.md)
- [ADR-0024 — NuGet-Packaging & Versionierung](../adr/0024-nuget-packaging-und-versionierung.md)
