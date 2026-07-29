# EBICO — Ticket overview

Quick reference over all milestones and issues that `create-ebico-plan.sh`
creates. Status: planning phase.

**Total:** 10 milestones · 64 issues (of which 12 epics) · 16 labels

Every feature issue automatically carries a project-wide *Definition of Done*
(Markdown doc under `docs/` **and** unit tests). Epics do not carry it.

---

## Project structure (target picture)

5 projects: `EBICO.Core` (shared primitives), `EBICO.Connector` (NuGet client),
`EBICO.Server` (emulator), `EBICO.Suite` (Blazor UI), `EBICO.Tests`.

Supported versions: **H003 / H004 / H005**. Order coverage: as complete a
BTF/order palette as possible.

---

## M0 — Foundation & Tooling

Foundation, cross-cutting requirements, procurement & law.

- **EPIC:** Foundation & Tooling
- **EPIC:** Documentation strategy (Markdown / `docs/`)
- **EPIC:** Test strategy (unit tests per feature)
- Obtain schemas & specs (procurement script)
- License/Terms-of-Use clarification (EBICS schemas/specs)
- Create solution & project skeleton
- CI pipeline (GitHub Actions)
- Test harness & fixtures
- Document architecture decisions (ADRs)

## M1 — Core & Protocol Primitives

Shared protocol foundations in `EBICO.Core`.

- **EPIC:** Core & Protocol Primitives
- Generate XSD bindings — H005 (EBICS 3.0)
- Generate XSD bindings — H004 (EBICS 2.5)
- Generate XSD bindings — H003 (EBICS 2.4)
- Version abstraction / protocol dispatch
- XML serialisation & canonicalization (C14N)
- Domain model: bank / partner / user / subscriber

## M2 — Cryptography & Certificates

Signature, encryption, hashing, certificates.

- **EPIC:** Cryptography & Certificates
- Key pairs & representation (A/E/X)
- Bank-technical signature A005/A006 (sign + verify)
- Authentication signature X002
- Encryption E002 (RSA + AES)
- Hashing & public-key fingerprints (HPB/INI/HIA)
- Certificate verification (X.509)

## M3 — Server: Key Management

Subscriber onboarding in the emulator.

- **EPIC:** Server — Key Management & Onboarding
- Hostable server skeleton (ASP.NET Core)
- INI — send the signature keys (A00x)
- HIA — send auth & enc keys (X002/E002)
- HPB — retrieve the bank keys
- HSA / SPR / HCA / HCS — key change & suspension
- Subscriber/partner/bank management (master data)

## M4 — Server: Transaction Engine

Generic upload/download transaction machine.

- **EPIC:** Server — Transaction Engine
- Upload transaction (Initialisation + Transfer)
- Download transaction (Initialisation + Transfer + Receipt)
- Segmentation, compression & Base64 pipeline
- Transaction recovery & timeouts
- EBICS return-code catalogue

## M5 — Server: Orders & BTF

Order types / Business Transaction Formats.

- **EPIC:** Orders & Business Transaction Formats
- BTF framework (H005)
- Upload orders: payments (CCT/CDD/CDB/CIP/…)
- Download orders: statements & reports (STA/C53/C52/C54/Z53…)
- Status & protocol orders (HAC/HAA/HTD/HKD/HPD/PTK)
- Distributed electronic signature (HVE/HVD/HVU/HVZ/HVS/HVT)
- Maintain the order/BTF coverage matrix

## M6 — Connector (NuGet)

Client library (mediator pattern). Architecture: `docs/connector/architecture.md`.

- **EPIC:** EBICO.Connector (NuGet Client) — contains the full architecture
- Architecture documentation EBICO.Connector
- Client core & configuration
- Onboarding flows: INI / HIA / HPB
- Upload API (CCT/CDD …)
- Download API (STA/C53 …)
- NuGet packaging & samples

## M7 — Suite (Blazor UI)

Admin/inspector UI for the emulator.

- **EPIC:** EBICO.Suite (Blazor UI)
- UI skeleton & navigation
- Master-data management (banks/partner/user)
- Transaction inspector
- Key/certificate view

## M8 — Validation & Conformance

End-to-end, negative cases, real clients.

- **EPIC:** Validation & Conformance
- E2E: Connector ↔ Server happy paths
- Negative/security cases
- Conformance against real clients

## M9 — Packaging & Docs

Publication and documentation.

- **EPIC:** Packaging & Documentation
- Container image for EBICO.Server
- NuGet publish pipeline
- Quickstart & samples

---

## Recommended order

The milestones are intended as a dependency chain:

```
M0 → M1 → M2 → M3 → M4 → M5 → M6 → M7 → M8 → M9
```

In practice, after M0/M1/M2 (foundation + protocol + crypto) a
split is worthwhile: the **server strand** (M3 → M4 → M5) and the **connector strand** (M6)
can partly run in parallel, because both build on `EBICO.Core`. M7 (UI)
needs a working server; M8/M9 come at the end.

> Important: before M1, clarify the licensing question from M0 (may XSDs/bindings go into the repo?)
> and obtain the schemas via `scripts/fetch-schemas.sh`.
