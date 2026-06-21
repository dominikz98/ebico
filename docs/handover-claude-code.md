# Handover-Prompt für Claude Code

Kopiere den folgenden Block als erste Nachricht in Claude Code (im Wurzel-
verzeichnis des `ebico`-Repos). Er gibt den nötigen Kontext, ohne dass du alles
neu erklären musst.

---

```
Du arbeitest am Projekt EBICO: einer EBICS-Implementierung in C# (.NET 10),
konzeptionell wie Azurite, aber für EBICS statt Azure Storage. Ziel ist ein
Server-Emulator plus ein Client-Package.

## Projektstruktur (5 Projekte, noch anzulegen)
- EBICO.Core      — geteilte Primitives (Schemas/Serialisierung, Krypto, BTF/Order-Modelle)
- EBICO.Connector — NuGet-Client für den Zugriff auf einen EBICS-Server
- EBICO.Server    — der Emulator (hostable, ASP.NET Core)
- EBICO.Suite     — Blazor-UI für den Server
- EBICO.Tests     — Unit-/Integration-/Conformance-Tests

Unterstützte EBICS-Versionen: H003, H004, H005. Order-Abdeckung: möglichst
vollständige BTF/Order-Palette.

## Was schon existiert (im Repo)
- GitHub-Issues & Milestones sind bereits angelegt (10 Milestones M0–M9,
  64 Issues, 12 Epics). Übersicht: docs/ticket-overview.md
- scripts/fetch-schemas.sh — beschafft/sortiert die EBICS-XSDs reproduzierbar
- docs/protocol/schema-sources.md — Quell-URLs, Dateiliste, Lizenzlage
- docs/connector/architecture.md — Architektur des Connectors (Mediator-Muster)
- .gitignore — schließt schemas/**/*.xsd aus (Lizenz!)

## Projektweite, verbindliche Regeln (Definition of Done je Feature)
1. DOKU: Jedes Feature wird in Markdown unter docs/ dokumentiert und im
   Doku-Index verlinkt. Docs-as-Code: Doku gehört in denselben PR wie der Code.
2. TESTS: Jedes Feature wird mit Unit-Tests abgesichert (Happy Path +
   Negativ-/Grenzfälle). Protokoll-/Krypto-Logik gegen Testvektoren und
   Sample-XML, nicht nur Selbstkonsistenz. Kein Feature gilt ohne Tests als fertig.
3. CI muss grün sein (dotnet build + dotnet test, keine neuen Warnungen).
4. XML-Doc-Kommentare an öffentlichen APIs.

## Wichtige Randbedingungen
- LIZENZ: Die EBICS-Schemas/Specs sind proprietär (EBICS SC). Modifikation /
  derivative uses ohne Genehmigung nicht erlaubt. XSDs NICHT ungeprüft committen
  — vor M1 die Lizenzfrage klären (Issue „Lizenz-/Terms-of-Use-Klärung"). Schemas
  per scripts/fetch-schemas.sh lokal beziehen.
- Die Architektur in docs/connector/architecture.md ist ein begründeter
  Vorschlag, KEIN gegen die Spec verifiziertes Design. Sobald die echten Schemas
  vorliegen, Details (z. B. Reihenfolge E002/A00x/X002, Segmentschleife je
  Version) gegen die offiziellen XSDs/Annexe verifizieren und Doku nachziehen.

## Connector-Architektur in Kürze (Details: docs/connector/architecture.md)
Mediator-Muster: der Aufrufer kennt nur IEbicsClient.Send(request) und bekommt
ein typisiertes EbicsResult<T>. Pipeline pro Send: Validierung → Serialisierung →
Komprimieren/E002/A00x → X002 → Transport (HttpClient hinter ITransport) →
Verify/Entschlüsseln → Returncode → ggf. Segmente → Deserialisieren. Eigener
Dispatch statt MediatR. Key-Store als Abstraktion (IKeyStore).

## Womit ich jetzt starten will
Beginne mit Milestone M0. Konkret zuerst:
1. „Solution & Projektgerüst anlegen": EBICO.sln mit den 5 Projekten (net10.0),
   Directory.Build.props (Nullable enable, TreatWarningsAsErrors), zentrale
   Paketverwaltung (Directory.Packages.props), .editorconfig, docs/-Grundstruktur,
   .github/PULL_REQUEST_TEMPLATE.md mit Doku-/Test-Checkliste, Solution-Folder src/ tests/ docs/.
2. Danach „Test-Harness & Fixtures" (xUnit + FluentAssertions) und
   „CI-Pipeline (GitHub Actions)".

Lies zuerst docs/ticket-overview.md und die genannten Doku-Dateien, schau dir
die offenen GitHub-Issues an (gh issue list), und schlag mir dann einen
konkreten Plan für das Solution-Gerüst vor, bevor du Dateien anlegst. Arbeite
issue-getrieben: pro Issue ein Branch + PR, Doku und Tests inklusive.
```

---

## Tipps zur Nutzung

- **Vor dem Start** sicherstellen, dass `gh` authentifiziert ist und Claude Code
  im Repo-Wurzelverzeichnis läuft — dann kann es `gh issue list` selbst nutzen.
- Wenn du lieber issue-für-issue vorgehst, ersetze den letzten Absatz durch eine
  konkrete Issue-Nummer: *„Arbeite jetzt Issue #12 ab."*
- Den Block kannst du auch als `CLAUDE.md` ins Repo legen — dann hat Claude Code
  den Kontext in jeder Session automatisch, ohne dass du ihn einfügst.
