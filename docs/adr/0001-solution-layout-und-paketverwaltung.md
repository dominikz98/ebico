# 0001 — Solution-Layout & zentrale Paketverwaltung

- Status: accepted
- Datum: 2026-06-21

## Kontext

EBICO besteht aus mehreren Komponenten (geteilte Primitives, Client, Server, UI,
Tests), die auf .NET 10 zielen. Es braucht ein konsistentes, reproduzierbares
Build-Setup mit einheitlichen Konventionen über alle Projekte.

## Entscheidung

- **Fünf Projekte** unter Solution-Foldern `src/` und `tests/`: `EBICO.Core`,
  `EBICO.Connector`, `EBICO.Server`, `EBICO.Suite`, `EBICO.Tests`. Referenzgraph:
  Connector/Server/Suite → Core; Tests → Core/Connector/Server.
- **`Directory.Build.props`** setzt projektweit `Nullable`, `ImplicitUsings`,
  `TreatWarningsAsErrors=true`, `AnalysisLevel=latest`. `GenerateDocumentationFile`
  (XML-Doc-Pflicht) nur für die Bibliotheken `Core` + `Connector`.
- **Zentrale Paketverwaltung** (`Directory.Packages.props`,
  `ManagePackageVersionsCentrally=true`) mit transitivem Pinning
  (`CentralPackageTransitivePinningEnabled=true`).
- **Keine `packages.lock.json` / kein `--locked-mode`.** Das implizite
  Blazor-Asset-Paket `Microsoft.AspNetCore.App.Internal.Assets` ist an den
  SDK-Runtime-Patch gebunden; eingecheckte Lock-Files brechen `--locked-mode`
  zwischen Maschinen mit unterschiedlichem SDK-Patch (NU1004). Reproduzierbarkeit
  kommt aus der exakten Versionspinnung der CPM; das SDK wird über `global.json`
  gepinnt.

Details: [../development/solution-layout.md](../development/solution-layout.md).

## Konsequenzen

- Einheitliche Compiler-/Analyzer-Politik; jede neue Warnung bricht den Build (DoD).
- Genau eine Stelle pro Paketversion, keine Versions-Drift.
- Reproduzierbare Restores ohne den Lock-File-Wartungsaufwand.
- Trade-off: Reproduzierbarkeit hängt an exakten CPM-Versionen + SDK-Pin statt an
  Lock-Files; bei SDK-Wechsel ist `global.json` bewusst zu aktualisieren.

## Alternativen

- **`packages.lock.json` + `--locked-mode`:** stärkere Garantie, aber durch die
  SDK-gebundenen Blazor-Assets in CI nicht praktikabel (NU1004) — verworfen.
- **Pro-Projekt-Versionen ohne CPM:** verworfen wegen Versions-Drift-Risiko.
