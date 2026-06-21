# EBICO — Kontext für Claude Code

Du arbeitest am Projekt **EBICO**: einer EBICS-Implementierung in C# (.NET 10),
konzeptionell wie *Azurite*, aber für EBICS statt Azure Storage. Ziel ist ein
Server-Emulator plus ein Client-Package.

## Projektstruktur (5 Projekte)

- `src/EBICO.Core` — geteilte Primitives (Schemas/Serialisierung, Krypto, BTF/Order-Modelle)
- `src/EBICO.Connector` — NuGet-Client für den Zugriff auf einen EBICS-Server (Mediator-Muster)
- `src/EBICO.Server` — der Emulator (hostbar, ASP.NET Core)
- `src/EBICO.Suite` — Blazor-UI (Blazor Web App, Interactive Server) für den Server
- `tests/EBICO.Tests` — Unit-/Integration-/Conformance-Tests (xUnit v3)

Projektreferenzen: Connector→Core, Server→Core, Suite→Core, Tests→{Core, Connector, Server}.

Unterstützte EBICS-Versionen: **H003, H004, H005**. Order-Abdeckung: möglichst
vollständige BTF/Order-Palette.

## Build & Tooling

- **.NET 10**, SDK gepinnt via `global.json`.
- `Directory.Build.props`: `Nullable enable`, `ImplicitUsings`, `TreatWarningsAsErrors`,
  `RestorePackagesWithLockFile`. XML-Doc-Pflicht (`GenerateDocumentationFile`) nur
  für `EBICO.Core` + `EBICO.Connector`.
- **Zentrale Paketverwaltung** (`Directory.Packages.props`): Versionen NUR dort,
  `PackageReference` in den `.csproj` ohne Version-Attribut.
- Test: **xUnit v3** + **AwesomeAssertions** (MIT-Fork von FluentAssertions v7;
  FluentAssertions v8 ist kommerziell lizenziert → bewusst NICHT verwendet).

## Projektweite, verbindliche Regeln (Definition of Done je Feature)

1. **DOKU:** Jedes Feature wird in Markdown unter `docs/` dokumentiert und im
   Doku-Index (`docs/index.md`) verlinkt. Docs-as-Code: Doku gehört in denselben
   PR wie der Code.
2. **TESTS:** Jedes Feature wird mit Unit-Tests abgesichert (Happy Path +
   Negativ-/Grenzfälle). Protokoll-/Krypto-Logik gegen Testvektoren und
   Sample-XML, nicht nur Selbstkonsistenz. Kein Feature gilt ohne Tests als fertig.
3. **CI grün:** `dotnet build` + `dotnet test`, keine neuen Warnungen.
4. **XML-Doc-Kommentare** an öffentlichen APIs.
5. **Code-Review** durchgeführt.

## Arbeitsweise

- **Issue-getrieben:** pro Issue ein Branch (`feat/<nr>-<slug>`) + ein PR, Doku
  und Tests inklusive. PR-Body nutzt `.github/PULL_REQUEST_TEMPLATE.md` und enthält
  `Closes #<nr>`.
- Issues/Milestones liegen auf GitHub (`gh issue list`). Übersicht:
  `docs/ticket-overview.md` (10 Milestones M0–M9, 63 Issues, 12 Epics).
- Roadmap: M0 (Fundament) → M1 (Core/Protokoll) → M2 (Krypto) → M3–M5 (Server)
  → M6 (Connector) → M7 (Suite) → M8 (Conformance) → M9 (Packaging).

## Wichtige Randbedingungen

- **LIZENZ:** Die EBICS-Schemas/Specs sind proprietär (EBICS SC). Modifikation /
  derivative uses ohne Genehmigung nicht erlaubt. XSDs und offizielle Beispiel-XML
  werden **nicht** ins Repo committet (`.gitignore`); lokal via
  `scripts/fetch-schemas.sh` beziehen. Lizenzfrage vor M1 klären (Issue #5).
- Die Architektur in `docs/connector/architecture.md` ist ein begründeter
  Vorschlag, **kein** gegen die Spec verifiziertes Design. Sobald die echten
  Schemas vorliegen, Details (z. B. Reihenfolge E002/A00x/X002, Segmentschleife je
  Version) gegen die offiziellen XSDs/Annexe verifizieren und Doku nachziehen.

## Connector-Architektur in Kürze (Details: `docs/connector/architecture.md`)

Mediator-Muster: der Aufrufer kennt nur `IEbicsClient.Send(request)` und bekommt
ein typisiertes `EbicsResult<T>`. Pipeline pro `Send`: Validierung → Serialisierung
→ Komprimieren/E002/A00x → X002 → Transport (HttpClient hinter `ITransport`) →
Verify/Entschlüsseln → Returncode → ggf. Segmente → Deserialisieren. Eigener
Dispatch statt MediatR. Key-Store als Abstraktion (`IKeyStore`).
