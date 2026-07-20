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

Projektreferenzen: Connector→Core, Server→Core, Suite→{Core, Server}, Tests→{Core, Connector, Server}.
(Suite→Server seit #53: die Blazor-UI nutzt den `IMasterDataManager`/State-Store in-process, ADR-0009.)

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
  `scripts/fetch-schemas.sh` beziehen. Die **generierten C#-Bindings** unter
  `src/EBICO.Core/Schema/` **werden hingegen committet** (ADR-0006, Option B; via
  `scripts/generate-bindings.sh` reproduzierbar) — so baut/testet die CI ohne
  Schemas. Genehmigung der EBICS SC wird parallel verfolgt.
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

## Doku-Landkarte (Einstiegspunkte)

- `docs/index.md` — annotierter Gesamtindex; **immer zuerst** nachschlagen.
- `docs/server/order-coverage-matrix.md` — **Source of Truth** für OrderType/BTF ×
  Version × Status. Per Guard-Test (`OrderCoverageMatrixTests`) mit den Code-Katalogen
  synchron gehalten; enthält einen eigenen Lücken-Abschnitt.
- `docs/adr/README.md` — 25 ADRs (0001–0025, MADR-lite, alle `accepted`) + Backlog
  offener/abgelöster Entscheidungen. Jede größere Designfrage ist hier begründet.
- `docs/ticket-overview.md` — Milestones (M0–M9), Issues, Epics.
- Feature-Doku liegt thematisch unter `docs/<bereich>/<name>.md`
  (`protocol/`, `server/`, `connector/`, `suite/`, `development/`, `deployment/`, `legal/`).

## Querschnittliche Code-Konventionen

- **Multi-Version-Dispatch (H003/H004/H005):** durchgängiges Leitmotiv. Pro Feature eine
  versionsagnostische Base-Klasse (`<Xxx>OrderHandlerBase`) + je Version eine Subklasse
  (`H003<Xxx>OrderHandler` …). Tests spannen die Version×Fall-Matrix via `TheoryData`.
- **DI-Registrierung (`AddEbicoServer` in `EbicoServerServiceCollectionExtensions.cs`):**
  Infrastruktur-Dienste (Stores, Verifier, Resolver) mit `TryAddSingleton` (überschreibbar).
  Mehrfach-Extension-Points dagegen mit `AddSingleton` (NICHT `TryAdd`), damit mehrere
  koexistieren: Order-Handler (`IEbicsOrderHandler`, Auflösung via
  `IEbicsOrderHandlerResolver` keyed nach `(Version, OrderType)`) sowie Upload-/Download-
  Processoren (`IUploadOrderProcessor`/`IDownloadOrderProcessor`, Engine konsumiert das
  ganze `IEnumerable<…>`, erster `CanProcess`-Match gewinnt).
- **BTF/OrderType-Auflösung:** `BtfOrderTypeCatalog.Resolve{Upload,Download}OrderType`
  bildet alle drei Konventionen ab (H005 BTU/BTD+BTF · H003/H004 direkter Code ·
  H003/H004 FUL/FDL+FileFormat). Berechtigung: `Subscriber.HasPermissionFor` → `090003`.
- **Guard-Tests halten Doku↔Code synchron:** ein neuer OrderType muss in Katalog **und**
  Coverage-Matrix nachgezogen werden, sonst schlägt `OrderCoverageMatrixTests` fehl.
- **Test-Setup:** xUnit v3 + AwesomeAssertions; `TestContext.Current.CancellationToken`
  (Falle xUnit1051 unter `TreatWarningsAsErrors`); Server-Integrationstests via
  `extern alias EbicoServer` + `WebApplicationFactory<Program>`; E2E über `EbicsE2EHarness`
  + `E2EKeyPool` (RSA-2048 ist harte Untergrenze ⇒ Schlüssel-Wiederverwendung);
  XML-Vergleich mit `CanonicalXmlComparer`; proprietäre Sample-XML „skip-if-missing".
- **Spec-Vorbehalte (aktueller Stand):** serverseitige **X002-Verifikation ist aktiv**
  (`X002EbicsRequestVerifier`, ADR-0023/#58, greift erst nach HIA). **ES/A00x-Signaturprüfung
  der OrderData bleibt zurückgestellt**; kein Key-Gültigkeitsfenster; Server-Antworten
  unsigniert. Teile der Architektur sind Design-Intent, noch nicht gegen die offiziellen
  XSDs verifiziert (Schemas proprietär).

## Verfügbare Skills (`.claude/skills/`)

Abrufbare Schritt-für-Schritt-Rezepte für die wiederkehrenden Abläufe:

- `ebics-order-handler` — neuen serverseitigen Order-Handler bzw. Upload-/Download-Processor anlegen.
- `ebics-conformance-test` — E2E-/Conformance-Tests schreiben (Round-Trip, Wire-Shapes, Vendor-Captures, Tampering).
- `ebics-feature-workflow` — kompletter Feature-/Bugfix-Ablauf inkl. Definition of Done (Branch → Doku → ADR → Tests → PR).
- `ebics-crypto` — EBICS-Krypto (A005/A006, X002, E002, Fingerprints, X.509).
- `ebics-suite` — an der Blazor-Suite arbeiten (Seiten/Komponenten, Stammdaten, Inspektor, Schlüssel-Ansicht).
- `ebics-connector` — am Connector-NuGet-Paket arbeiten (Send-Pipeline, DI, Sende-Validierung, Packaging).

## Wartung von Kontext, Doku & Skills

Diese Kontextdateien pflegen sich **nicht** selbst. Ihre Aktualisierung ist Teil der Definition
of Done und gehört in **denselben PR** wie die auslösende Änderung:

- **Doku (`docs/`):** neue/geänderte Features dokumentieren und in `docs/index.md` verlinken;
  bei Auftragsarten `docs/server/order-coverage-matrix.md` nachziehen (Guard-Test erzwingt das).
- **`CLAUDE.md`:** anpassen, sobald sich eine querschnittliche Konvention, die Projektstruktur
  oder ein Spec-Vorbehalt ändert.
- **Skills (`.claude/skills/`):** aktualisieren, wenn sich ein beschriebener Ablauf oder ein
  referenziertes Symbol/Pfad ändert (z. B. Umbenennung eines Handlers, Interfaces oder einer
  Doku-Seite). Die Skills verweisen bewusst auf konkrete Dateien/Typen und veralten sonst
  **stillschweigend** — es gibt dafür keinen automatischen Wächter.

Faustregel: Berührt ein PR ein Muster, das in `CLAUDE.md` oder einem Skill beschrieben ist,
gehört die Aktualisierung dieses Textes in denselben PR. Die PR-Checkliste
(`.github/PULL_REQUEST_TEMPLATE.md`) fragt „Docs/Skills aktualisiert?" explizit ab und verlangt
eine Issue-Verlinkung (`Closes #<nr>`) — **jeder** PR referenziert genau ein Issue, auch reine
Tooling-/Meta-Änderungen (z. B. an `.claude/` oder `CLAUDE.md` selbst).
