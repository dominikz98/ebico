# 0004 — Multi-Version-Strategie (H003/H004/H005)

- Status: accepted
- Datum: 2026-06-21 (in M1 gegen die echten Schemas verifiziert)

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
- **In M1 verifiziert (`accepted`):** Die XSD-Bindings (#11–#13) sind pro Version in
  eigenen Namespaces realisiert — `EBICO.Core.Schema.{H003,H004,H005}` —, während
  die echt geteilten Schemas (xmldsig, HEV/H000, Signatur S001/S002) **einmal**
  unter `EBICO.Core.Schema.{XmlDsig,Hev,Signature.S001,Signature.S002}` liegen
  (Layout-Details: [../protocol/xsd-bindings.md](../protocol/xsd-bindings.md)).
  Offene Detailpunkte zum Ablauf (Reihenfolge E002/A00x/X002, Segmentschleife je
  Version) werden mit der Krypto-/Transport-Arbeit (M2 ff.) gegen die Annexe
  konkretisiert.

## Alternativen

- **Pro Version komplett getrennte Stacks:** maximale Klarheit, aber massive
  Duplikation — verworfen.
- **Nur Neueste (H005) zuerst, ältere später:** schneller Start, widerspricht aber
  dem Ziel breiter Versionsabdeckung — als Reihenfolge denkbar, nicht als Architektur.
