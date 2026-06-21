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
| [0004](0004-multi-version-strategie.md) | Multi-Version-Strategie (H003/H004/H005) | proposed |
| [0005](0005-connector-dispatch-ohne-mediatr.md) | Connector: eigener Dispatch statt MediatR | accepted |

## Offene/geplante Entscheidungen (Backlog)

Themen, die eine eigene ADR bekommen, sobald sie anstehen:

- **Generierte XSD-Bindings = derivative work?** — Gate für M1; Optionen in
  [../legal/ebics-licensing.md](../legal/ebics-licensing.md). ADR folgt mit der
  Entscheidung (Bindings committen ja/nein).
- **Serialisierungstechnik** der XSD-Bindings (XmlSerializer vs. generiert vs.
  manuell) — M1 (#11–#13, #15).
- **Krypto-Bibliothek** (System.Security.Cryptography vs. BouncyCastle) — M2 (#17 ff.).
- **Persistenz des Server-States** (In-Memory-Default, pluggable Store) — M3/M4.
- **Returncode-Modellierung** (`EbicsResult<T>` vs. Exceptions, Katalog) — M4 (#36).
