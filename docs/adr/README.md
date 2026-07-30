# Architecture Decision Records (ADRs)

This is where EBICO's **architecture decisions** are recorded. Belongs to
issue **#9 — Document architecture decisions (ADRs)** (Milestone M0).

## Format

One file per decision, `NNNN-shorttitle.md` (numbered consecutively). Structure
(modelled on [MADR](https://adr.github.io/madr/)):

```
# NNNN — Title
- Status: proposed | accepted | superseded by NNNN | deprecated
- Date: YYYY-MM-DD

## Context
## Decision
## Consequences
## Alternatives   (optional)
```

**Status legend:** `proposed` (proposed, still open), `accepted`
(decided/implemented), `superseded`/`deprecated` (superseded). An ADR is not deleted
but marked as superseded and pointed to the successor ADR.

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-solution-layout-und-paketverwaltung.md) | Solution layout & central package management | accepted |
| [0002](0002-test-stack.md) | Test stack: xUnit v3 + AwesomeAssertions | accepted |
| [0003](0003-umgang-mit-proprietaeren-schemas.md) | Handling proprietary EBICS schemas | accepted |
| [0004](0004-multi-version-strategie.md) | Multi-version strategy (H003/H004/H005) | accepted |
| [0005](0005-connector-dispatch-ohne-mediatr.md) | Connector: custom dispatch instead of MediatR | accepted |
| [0006](0006-generierte-xsd-bindings-committen.md) | Commit generated XSD bindings (Option B) | accepted |
| [0007](0007-domaenen-value-objects-record-struct.md) | Domain value objects as `readonly record struct` | accepted |
| [0008](0008-krypto-bibliothek.md) | Crypto library: `System.Security.Cryptography` | accepted |
| [0009](0009-blazor-render-mode.md) | Blazor render mode (Interactive Server) | accepted |
| [0010](0010-pdf-bibliothek.md) | PDF library for the INI/HIA letter: QuestPDF (Community) | accepted |
| [0011](0011-server-stammdatenverwaltung.md) | Server master-data management (manager over store, admin API) | accepted |
| [0012](0012-returncode-katalog.md) | EBICS return-code catalogue (modelling & placement) | accepted |
| [0013](0013-upload-transaktions-engine.md) | Upload transaction engine & store | accepted |
| [0014](0014-download-transaktions-engine.md) | Download transaction engine, store & data provisioning | accepted |
| [0015](0015-ereignis-protokollspeicher.md) | Event/audit log store (`IEventLog`) | accepted |
| [0016](0016-btf-framework-und-berechtigung.md) | BTF framework & authorisation check | accepted |
| [0017](0017-zahlungsverkehr-order-verarbeitung.md) | Payment order processing (validation & status-report storage) | accepted |
| [0018](0018-kontoauszug-download-orders.md) | Account-statement/report download orders (synthetic generation, camt.05x.001.08, ZIP container) | accepted |
| [0019](0019-status-protokoll-orders.md) | Status & protocol orders (domain extension, HAC/PTK as IEventLog projection) | accepted |
| [0020](0020-veu-orders.md) | Distributed electronic signature (VEU store, parking/signing workflow) | accepted |
| [0021](0021-message-capture-store.md) | Message-capture store (`IMessageCaptureStore`, raw XML per transaction) | accepted |
| [0022](0022-container-image-und-konfiguration.md) | Container image & ENV configuration (multi-stage, `PROJECT` arg, `Ebico` config binding) | accepted |
| [0023](0023-serverseitige-x002-verifikation.md) | Server-side X002 authentication-signature verification | accepted |
| [0024](0024-nuget-packaging-und-versionierung.md) | NuGet packaging & versioning (CalVer) of the connector | accepted |
| [0025](0025-clientseitige-sende-validierung.md) | Client-side send validation (authorisation/BTF) in the connector | accepted |
| [0026](0026-konformitaet-gegen-reale-clients.md) | Conformance against real clients (vendor captures, test tiers, deviation policy) | accepted |
| [0027](0027-nuget-publish-und-release-pipeline.md) | NuGet publish & release pipeline (nuget.org, tag-driven, GHCR container push) | accepted |
| [0028](0028-branch-protection-main.md) | Branch protection for `main`: CI as an enforced merge gate (required checks, `enforce_admins`, no review requirement) | accepted |
| [0029](0029-interop-fixes-reale-clients.md) | Interop fixes for real clients (`OrderDetails` without `xsi:type`, `A006` on H004, modulus normalisation) | accepted |
| [0030](0030-defaults-und-clientseitige-veu-anbindung.md) | Aligned transport defaults (segment size ↔ body limit), consistent return-code texts and client-side VEU wiring | accepted |
| [0031](0031-stammdaten-inseln-aenderungsbenachrichtigung.md) | Change notification between the Suite's master-data islands (`IMasterDataChangeNotifier`, singleton) | accepted |

## Open/planned decisions (backlog)

Topics that get their own ADR once they come up:

- ~~**Generated XSD bindings = derivative work?**~~ — **decided** in
  [ADR-0006](0006-generierte-xsd-bindings-committen.md) (Option B: commit the
  bindings, XSDs stay untracked).
- ~~**Serialisation technique** of the XSD bindings~~ — **decided**: generated via
  XmlSchemaClassGenerator (XmlSerializer classes), see
  [../protocol/xsd-bindings.md](../protocol/xsd-bindings.md) and ADR-0006.
- ~~**Crypto library** (System.Security.Cryptography vs. BouncyCastle)~~ — **decided**
  in [ADR-0008](0008-krypto-bibliothek.md) (System.Security.Cryptography, no BouncyCastle).
- ~~**Persistence of the server state** (in-memory default, pluggable store)~~ — **decided** in
  [ADR-0011](0011-server-stammdatenverwaltung.md) (in-memory default, pluggable via `TryAddSingleton`;
  master-data management as a manager over the store, #30). A concrete persistent store stays open if needed.
- **Persistent store (SQLite or similar)** — open. [ADR-0015](0015-ereignis-protokollspeicher.md) keeps the
  event/audit log store (`IEventLog`) deliberately in-memory + async-pluggable; the `IEventLog` is the
  first candidate for a real persistence implementation (would then get its own ADR).
- ~~**Return-code modelling** (`EbicsResult<T>` vs. exceptions, catalogue)~~ — **decided** in
  [ADR-0012](0012-returncode-katalog.md) (central catalogue + registry in `EBICO.Core.ReturnCodes`,
  mapping server-side, technical/business separated).
