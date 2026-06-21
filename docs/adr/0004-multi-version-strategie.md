# 0004 — Multi-Version-Strategie (H003/H004/H005)

- Status: proposed
- Datum: 2026-06-21

## Kontext

EBICO unterstützt drei EBICS-Protokollversionen — **H003** (2.4), **H004** (2.5),
**H005** (3.0). Sie unterscheiden sich in Schemas, Order-/BTF-Modell und teils im
Transaktionsablauf. Core, Server und Connector müssen versionsabhängig arbeiten,
ohne die Logik dreifach zu duplizieren. Der konkrete Entwurf wird in M1 gegen die
echten Schemas verifiziert (siehe Randbedingung in `CLAUDE.md`).

## Entscheidung (vorgeschlagen)

- Eine zentrale **`EbicsVersion`**-Abstraktion in `EBICO.Core` (Enum bereits
  vorhanden: `H003`/`H004`/`H005`) als Dreh- und Angelpunkt.
- **Versions-Dispatch in Core** (Issue #14): gemeinsame Abläufe einmal, nur die
  versionsspezifischen Teile (Schema-Bindings, Order-/BTF-Mapping, evtl.
  Segment-/Krypto-Details) hinter versionsselektierten Implementierungen.
- **Pro Version getrennte XSD-Bindings** (Issues #11–#13) in eigenen
  Namespaces/Ordnern, hinter gemeinsamen Schnittstellen.

## Konsequenzen

- Eine Stelle, an der die Zielversion gewählt wird (vgl. Connector-DI:
  `o.Version = EbicsVersion.H005`).
- Gemeinsame Logik bleibt versionsunabhängig testbar; Unterschiede sind lokal.
- **Status `proposed`:** Details (z. B. Reihenfolge E002/A00x/X002, Segmentschleife
  je Version) sind erst gegen die offiziellen XSDs/Annexe in M1 zu verifizieren;
  diese ADR wird dann auf `accepted` aktualisiert oder verfeinert.

## Alternativen

- **Pro Version komplett getrennte Stacks:** maximale Klarheit, aber massive
  Duplikation — verworfen.
- **Nur Neueste (H005) zuerst, ältere später:** schneller Start, widerspricht aber
  dem Ziel breiter Versionsabdeckung — als Reihenfolge denkbar, nicht als Architektur.
