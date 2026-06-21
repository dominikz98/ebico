# EBICO — Ticket-Übersicht

Schnellreferenz über alle Milestones und Issues, die `create-ebico-plan.sh`
anlegt. Stand: Planungsphase.

**Gesamt:** 10 Milestones · 64 Issues (davon 12 Epics) · 16 Labels

Jedes Feature-Issue trägt automatisch eine projektweite *Definition of Done*
(Markdown-Doku unter `docs/` **und** Unit-Tests). Epics tragen sie nicht.

---

## Projektstruktur (Zielbild)

5 Projekte: `EBICO.Core` (geteilte Primitives), `EBICO.Connector` (NuGet-Client),
`EBICO.Server` (Emulator), `EBICO.Suite` (Blazor-UI), `EBICO.Tests`.

Unterstützte Versionen: **H003 / H004 / H005**. Order-Abdeckung: möglichst
vollständige BTF/Order-Palette.

---

## M0 — Foundation & Tooling

Fundament, Querschnittsanforderungen, Beschaffung & Recht.

- **EPIC:** Foundation & Tooling
- **EPIC:** Dokumentationsstrategie (Markdown / `docs/`)
- **EPIC:** Teststrategie (Unit-Tests pro Feature)
- Schemas & Specs beschaffen (Beschaffungsskript)
- Lizenz-/Terms-of-Use-Klärung (EBICS-Schemas/Specs)
- Solution & Projektgerüst anlegen
- CI-Pipeline (GitHub Actions)
- Test-Harness & Fixtures
- Architektur-Entscheidungen dokumentieren (ADRs)

## M1 — Core & Protocol Primitives

Geteilte Protokoll-Grundlagen in `EBICO.Core`.

- **EPIC:** Core & Protocol Primitives
- XSD-Bindings generieren — H005 (EBICS 3.0)
- XSD-Bindings generieren — H004 (EBICS 2.5)
- XSD-Bindings generieren — H003 (EBICS 2.4)
- Versionsabstraktion / Protokoll-Dispatch
- XML-Serialisierung & Canonicalization (C14N)
- Domänenmodell: Bank / Partner / User / Subscriber

## M2 — Cryptography & Certificates

Signatur, Verschlüsselung, Hashing, Zertifikate.

- **EPIC:** Cryptography & Certificates
- Schlüsselpaare & -repräsentation (A/E/X)
- Banktechnische Signatur A005/A006 (sign + verify)
- Authentifikationssignatur X002
- Verschlüsselung E002 (RSA + AES)
- Hashing & Public-Key-Fingerprints (HPB/INI/HIA)
- Zertifikatsverifizierung (X.509)

## M3 — Server: Key Management

Subscriber-Onboarding im Emulator.

- **EPIC:** Server — Key Management & Onboarding
- Hostable Server-Grundgerüst (ASP.NET Core)
- INI — Senden der Signaturschlüssel (A00x)
- HIA — Senden Auth- & Enc-Schlüssel (X002/E002)
- HPB — Abruf der Bankschlüssel
- HSA / SPR / HCA / HCS — Schlüsselwechsel & Sperrung
- Subscriber-/Partner-/Bank-Verwaltung (Stammdaten)

## M4 — Server: Transaction Engine

Generische Upload/Download-Transaktionsmaschine.

- **EPIC:** Server — Transaction Engine
- Upload-Transaktion (Initialisation + Transfer)
- Download-Transaktion (Initialisation + Transfer + Receipt)
- Segmentierung, Kompression & Base64-Pipeline
- Transaktions-Recovery & Timeouts
- EBICS-Returncode-Katalog

## M5 — Server: Orders & BTF

Order-Typen / Business Transaction Formats.

- **EPIC:** Orders & Business Transaction Formats
- BTF-Framework (H005)
- Upload-Orders: Zahlungsverkehr (CCT/CDD/CDB/CIP/…)
- Download-Orders: Kontoauszüge & Reports (STA/C53/C52/C54/Z53…)
- Status- & Protokoll-Orders (HAC/HAA/HTD/HKD/HPD/PTK)
- Verteilte elektronische Unterschrift (HVE/HVD/HVU/HVZ/HVS/HVT)
- Order-/BTF-Abdeckungsmatrix pflegen

## M6 — Connector (NuGet)

Client-Bibliothek (Mediator-Muster). Architektur: `docs/connector/architecture.md`.

- **EPIC:** EBICO.Connector (NuGet Client) — enthält die volle Architektur
- Architektur-Dokumentation EBICO.Connector
- Client-Kern & Konfiguration
- Onboarding-Flows: INI / HIA / HPB
- Upload-API (CCT/CDD …)
- Download-API (STA/C53 …)
- NuGet-Packaging & Beispiele

## M7 — Suite (Blazor UI)

Admin-/Inspektor-UI für den Emulator.

- **EPIC:** EBICO.Suite (Blazor UI)
- UI-Grundgerüst & Navigation
- Stammdaten-Verwaltung (Banks/Partner/User)
- Transaktions-Inspektor
- Schlüssel-/Zertifikats-Ansicht

## M8 — Validation & Conformance

End-to-End, Negativfälle, reale Clients.

- **EPIC:** Validation & Conformance
- E2E: Connector ↔ Server Happy Paths
- Negativ-/Sicherheitsfälle
- Konformität gegen reale Clients

## M9 — Packaging & Docs

Veröffentlichung und Doku.

- **EPIC:** Packaging & Documentation
- Container-Image für EBICO.Server
- NuGet-Publish-Pipeline
- Quickstart & Beispiele

---

## Empfohlene Reihenfolge

Die Milestones sind als Abhängigkeitskette gedacht:

```
M0 → M1 → M2 → M3 → M4 → M5 → M6 → M7 → M8 → M9
```

In der Praxis lohnt sich nach M0/M1/M2 (Fundament + Protokoll + Krypto) eine
Aufteilung: **Server-Strang** (M3 → M4 → M5) und **Connector-Strang** (M6)
können teils parallel laufen, weil beide auf `EBICO.Core` aufsetzen. M7 (UI)
braucht einen funktionierenden Server; M8/M9 kommen zum Schluss.

> Wichtig: Vor M1 die Lizenzfrage aus M0 klären (dürfen XSDs/Bindings ins Repo?)
> und die Schemas via `scripts/fetch-schemas.sh` beziehen.
