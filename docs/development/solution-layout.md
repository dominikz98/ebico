# Solution-Layout & Build-Konventionen

Diese Seite beschreibt, wie die EBICO-Solution aufgebaut ist und welche
projektweiten Build-Konventionen gelten. Sie gehört zu Issue **#6 — Solution &
Projektgerüst anlegen** (Milestone M0).

## Projekte

Die Solution `EBICO.sln` enthält fünf Projekte, physisch getrennt nach `src/`
(Produktivcode) und `tests/`:

| Projekt | SDK | Zweck |
| --- | --- | --- |
| `src/EBICO.Core` | `Microsoft.NET.Sdk` (classlib) | Geteilte Primitives: Schemas/Serialisierung, Krypto, BTF/Order-Modelle |
| `src/EBICO.Connector` | `Microsoft.NET.Sdk` (classlib) | NuGet-Client (Mediator-Muster) |
| `src/EBICO.Server` | `Microsoft.NET.Sdk.Web` | Der EBICS-Emulator (ASP.NET Core, hostbar) |
| `src/EBICO.Suite` | `Microsoft.NET.Sdk.Web` | Blazor Web App (Interactive Server) — Admin/Inspektor-UI |
| `tests/EBICO.Tests` | `Microsoft.NET.Sdk` | xUnit-v3-Testprojekt |

### Referenzgraph

```
EBICO.Core  ◄── EBICO.Connector
     ▲      ◄── EBICO.Server
     └────── ◄── EBICO.Suite

EBICO.Tests ──► EBICO.Core, EBICO.Connector, EBICO.Server
```

`EBICO.Suite → EBICO.Server` wird erst in M7 ergänzt, wenn die UI echte
Server-Daten anbindet. Für M0 referenziert die Suite nur `EBICO.Core`.

## Build-Konventionen

### `Directory.Build.props` (projektweit)

- `Nullable enable`, `ImplicitUsings enable`, `LangVersion latest`
- `TreatWarningsAsErrors=true` — Umsetzung der DoD „keine neuen Warnungen"
- `AnalysisLevel=latest` mit aktivierten .NET-Analyzern
- `EnforceCodeStyleInBuild=false` — Style-Regeln (`IDExxxx`) aus der
  `.editorconfig` leiten nur die IDE und brechen den Build **nicht**; echte
  Compiler-/Analyzer-Warnungen werden via `TreatWarningsAsErrors` hart
- `GenerateDocumentationFile=true` **nur** für `EBICO.Core` und
  `EBICO.Connector` (Bibliotheken mit öffentlicher API). Fehlende XML-Doc an
  öffentlichen Membern wird dort zum Build-Fehler (CS1591) — direkte Umsetzung
  der DoD „XML-Doc an öffentlichen APIs". Server/Suite sind Apps ohne
  veröffentlichte API-Fläche, Tests brauchen keine Doc-Files.

### Zentrale Paketverwaltung — `Directory.Packages.props`

`ManagePackageVersionsCentrally=true`. **Paketversionen stehen ausschließlich in
`Directory.Packages.props`** (`<PackageVersion …>`); in den `.csproj` werden
Pakete ohne Version-Attribut referenziert (`<PackageReference Include="…" />`).
So gibt es genau eine Stelle pro Version und keine Versions-Drift zwischen
Projekten. `CentralPackageTransitivePinningEnabled=true` pinnt zusätzlich auch
transitive Pakete — das liefert reproduzierbare Restores **ohne**
`packages.lock.json`.

> **Bewusst keine Lock-Files (`RestorePackagesWithLockFile`):** Das implizite
> Blazor-Asset-Paket `Microsoft.AspNetCore.App.Internal.Assets` ist an die
> ASP.NET-Runtime-Version des installierten SDK gebunden. Eingecheckte Lock-Files
> brechen damit `dotnet restore --locked-mode` zwischen Maschinen mit
> unterschiedlichem SDK-Patch (NU1004). Reproduzierbarkeit kommt hier aus der
> exakten Versionspinnung der zentralen Paketverwaltung; das SDK selbst ist über
> `global.json` gepinnt.

### `global.json`

Pinnt die .NET-SDK-Version (`rollForward: latestFeature`), damit lokale Builds
und CI dieselbe Toolchain verwenden.

### `.editorconfig`

.NET-Standardkonventionen: file-scoped Namespaces, `var`-Präferenzen, 4 Spaces
(C#) bzw. 2 Spaces (Projekt-/Konfig-Dateien), `_camelCase` für private Felder.

## Erste Primitive

`EBICO.Core` enthält bereits `EbicsVersion` (`H003`/`H004`/`H005`) — die
zentrale Versionsabstraktion, auf die u. a. die Connector-DI-Registrierung in
[../connector/architecture.md](../connector/architecture.md) verweist
(`o.Version = EbicsVersion.H005`). Ein Smoke-Test in `EBICO.Tests` prüft, dass
alle drei Versionen vorhanden sind.

## Verifikation

```bash
dotnet build EBICO.sln -c Release   # ohne Warnungen/Fehler
dotnet test                         # Smoke-Test grün
```
