# Architecture Decision Records (ADRs)

Hier werden **Architektur-Entscheidungen** von EBICO festgehalten. Gehört zu
Issue **#9 — Architektur-Entscheidungen dokumentieren (ADRs)** (Milestone M0).

## Format

Pro Entscheidung eine Datei `NNNN-kurztitel.md` (fortlaufend nummeriert). Aufbau
(angelehnt an [MADR](https://adr.github.io/madr/)):

```
# NNNN — Titel
- Status: proposed | accepted | superseded by NNNN | deprecated
- Datum: YYYY-MM-DD

## Kontext
## Entscheidung
## Konsequenzen
## Alternativen   (optional)
```

**Status-Legende:** `proposed` (vorgeschlagen, noch offen), `accepted`
(entschieden/umgesetzt), `superseded`/`deprecated` (abgelöst). Eine ADR wird nicht
gelöscht, sondern als abgelöst markiert und auf die Nachfolge-ADR verwiesen.

## Index

| ADR | Titel | Status |
| --- | --- | --- |
| [0001](0001-solution-layout-und-paketverwaltung.md) | Solution-Layout & zentrale Paketverwaltung | accepted |
| [0002](0002-test-stack.md) | Test-Stack: xUnit v3 + AwesomeAssertions | accepted |
| [0003](0003-umgang-mit-proprietaeren-schemas.md) | Umgang mit proprietären EBICS-Schemas | accepted |
| [0004](0004-multi-version-strategie.md) | Multi-Version-Strategie (H003/H004/H005) | accepted |
| [0005](0005-connector-dispatch-ohne-mediatr.md) | Connector: eigener Dispatch statt MediatR | accepted |
| [0006](0006-generierte-xsd-bindings-committen.md) | Generierte XSD-Bindings committen (Option B) | accepted |
| [0007](0007-domaenen-value-objects-record-struct.md) | Domänen-Value-Objects als `readonly record struct` | accepted |
| [0008](0008-krypto-bibliothek.md) | Krypto-Bibliothek: `System.Security.Cryptography` | accepted |
| [0009](0009-blazor-render-mode.md) | Blazor Render-Modus (Interactive Server) | accepted |
| [0010](0010-pdf-bibliothek.md) | PDF-Bibliothek für den INI-/HIA-Brief: QuestPDF (Community) | accepted |
| [0011](0011-server-stammdatenverwaltung.md) | Server-Stammdatenverwaltung (Manager über Store, Admin-API) | accepted |
| [0012](0012-returncode-katalog.md) | EBICS-Returncode-Katalog (Modellierung & Verortung) | accepted |
| [0013](0013-upload-transaktions-engine.md) | Upload-Transaktions-Engine & -Speicher | accepted |
| [0014](0014-download-transaktions-engine.md) | Download-Transaktions-Engine, -Speicher & Datenbereitstellung | accepted |
| [0015](0015-ereignis-protokollspeicher.md) | Ereignis-/Protokollspeicher (`IEventLog`) | accepted |
| [0016](0016-btf-framework-und-berechtigung.md) | BTF-Framework & Berechtigungsprüfung | accepted |
| [0017](0017-zahlungsverkehr-order-verarbeitung.md) | Zahlungsverkehr-Order-Verarbeitung (Validierung & Statusreport-Ablage) | accepted |
| [0018](0018-kontoauszug-download-orders.md) | Kontoauszug-/Report-Download-Orders (synthetische Generierung, camt.05x.001.08, ZIP-Container) | accepted |
| [0019](0019-status-protokoll-orders.md) | Status- & Protokoll-Orders (Domänen-Erweiterung, HAC/PTK als IEventLog-Projektion) | accepted |
| [0020](0020-veu-orders.md) | Verteilte elektronische Unterschrift (VEU-Speicher, Park-/Zeichnungs-Workflow) | accepted |
| [0021](0021-message-capture-store.md) | Message-Capture-Store (`IMessageCaptureStore`, Roh-XML je Transaktion) | accepted |
| [0022](0022-container-image-und-konfiguration.md) | Container-Image & ENV-Konfiguration (Multi-Stage, `PROJECT`-Arg, `Ebico`-Config-Binding) | accepted |
| [0023](0023-serverseitige-x002-verifikation.md) | Serverseitige X002-Authentifikationssignatur-Verifikation | accepted |
| [0024](0024-nuget-packaging-und-versionierung.md) | NuGet-Packaging & Versionierung (CalVer) des Connectors | accepted |
| [0025](0025-clientseitige-sende-validierung.md) | Clientseitige Sende-Validierung (Berechtigung/BTF) im Connector | accepted |

## Offene/geplante Entscheidungen (Backlog)

Themen, die eine eigene ADR bekommen, sobald sie anstehen:

- ~~**Generierte XSD-Bindings = derivative work?**~~ — **entschieden** in
  [ADR-0006](0006-generierte-xsd-bindings-committen.md) (Option B: Bindings
  committen, XSDs bleiben ungetrackt).
- ~~**Serialisierungstechnik** der XSD-Bindings~~ — **entschieden**: generiert via
  XmlSchemaClassGenerator (XmlSerializer-Klassen), siehe
  [../protocol/xsd-bindings.md](../protocol/xsd-bindings.md) und ADR-0006.
- ~~**Krypto-Bibliothek** (System.Security.Cryptography vs. BouncyCastle)~~ — **entschieden**
  in [ADR-0008](0008-krypto-bibliothek.md) (System.Security.Cryptography, kein BouncyCastle).
- ~~**Persistenz des Server-States** (In-Memory-Default, pluggable Store)~~ — **entschieden** in
  [ADR-0011](0011-server-stammdatenverwaltung.md) (In-Memory-Default, pluggbar via `TryAddSingleton`;
  Stammdatenverwaltung als Manager über dem Store, #30). Ein konkreter persistenter Store bleibt bei Bedarf offen.
- **Persistenter Store (SQLite o. ä.)** — offen. [ADR-0015](0015-ereignis-protokollspeicher.md) hält den
  Ereignis-/Protokollspeicher (`IEventLog`) bewusst In-Memory + async-pluggbar; der `IEventLog` ist der
  erste Kandidat für eine echte Persistenz-Implementierung (bekäme dann eine eigene ADR).
- ~~**Returncode-Modellierung** (`EbicsResult<T>` vs. Exceptions, Katalog)~~ — **entschieden** in
  [ADR-0012](0012-returncode-katalog.md) (zentraler Katalog + Registry in `EBICO.Core.ReturnCodes`,
  Mapping server-seitig, technisch/fachlich getrennt).
