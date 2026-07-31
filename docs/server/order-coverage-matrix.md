# Server: Order/BTF coverage matrix

> Implementation of **Issue #43** (Milestone M5 — Server: Orders & BTF). This page consolidates the
> concrete order implementations from #38–#42 into a **consolidated coverage matrix**: which
> order types/BTFs the emulator handles in which EBICS version (H003/H004/H005) and with which status —
> including flagged, open gaps.
>
> Deliberately **included**: a family-by-family overview of the entire order palette modelled so far
> (key management, generic transport orders, payments, account statements, status/protocol,
> VEU orders), the H005 BTF mapping of the business orders, and a dedicated gaps section.
> Deliberately **not yet**: completeness against the proprietary EBICS *BTF mapping / External Code
> List* (not in the repo, [licence](../legal/ebics-licensing.md)) and conformance against real clients
> (Milestone **M8**).

## Purpose

The order types are spread across the codebase (free strings throughout, grouped in static
catalogs — there is no central order-type enum). This page is the single, human-readable
overall overview of what the emulator covers. **The code remains the source of truth**; the matrix
is kept in sync by a guard test (`OrderCoverageMatrixTests`, see [Tests](#tests)), so no order type
registered in the code can silently drop out of the matrix.

The authoritative catalogs in the code:

- `EBICO.Core.Btf.BtfOrderTypeCatalog` (`All`) — BTF ↔ classic code for the business orders.
- `EBICO.Core.Payments.PaymentOrderTypes`, `EBICO.Core.Statements.StatementOrderTypes`,
  `EBICO.Core.Administrative.StatusProtocolOrderTypes`, `EBICO.Core.Administrative.VeuOrderTypes`.
- Server handler/engine constants (`EBICO.Server.Handlers.*OrderHandlerBase`,
  `EBICO.Server.Transactions.{Upload,Download}TransactionEngine`).

## Legend

- ✅ **implemented & tested** — handled by the emulator and covered by unit/integration tests.
- 🟡 **partial / best-effort** — functionally present, but with documented spec caveats
  (see [Open gaps](#open-gaps)).
- ❌ **planned / open** — not yet implemented (possibly present only as a schema binding).
- `–` **not applicable** — not intended for this version or for this order type.

The **H003 / H004 / H005** columns indicate whether the order type is supported by the emulator in
the respective protocol version. The **BTF (H005)** column names the H005 business transaction format
(`Service` / `MsgName`, and option where applicable) of the business orders; administrative/technical
orders remain `AdminOrderType`s in H005 and therefore carry `–`.

## Key management & onboarding

Classic key/onboarding order types. In all versions as a classic code or H005 `AdminOrderType` (no
BTF). See [INI](ini.md), [HIA](hia.md), [HPB](hpb.md),
[HCA/HCS/SPR/HSA](hca-hcs-spr-hsa.md).

| OrderType | Description | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `INI` | Send signature key (A00x) | – | ✅ | ✅ | ✅ | ✅ |
| `HIA` | Send authentication & encryption keys (X00x/E00x) | – | ✅ | ✅ | ✅ | ✅ |
| `HPB` | Retrieve public bank keys | – | ✅ | ✅ | ✅ | ✅ |
| `HCA` | Key change auth/enc | – | ✅ | ✅ | ✅ | ✅ |
| `HCS` | Key change signature + auth + enc | – | ✅ | ✅ | ✅ | ✅ |
| `SPR` | Suspend access (suspend subscriber) | – | ✅ | ✅ | ✅ | ✅ |
| `HSA` | Key transmission (legacy HIA) | – | ✅ | ✅ | ❌ | ✅ |

`HSA` is **removed in H005** (no H005 handler registered) and is therefore `❌` there.

## Generic transport orders (carriers)

The generic carrier order types over which H003/H004 and H005 transport business uploads/downloads.
The business identity sits in the `FileFormat` for H003/H004 and in the BTF for H005. See
[Upload transaction](upload-transaction.md) / [Download transaction](download-transaction.md).

| OrderType | Description | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `FUL` | Generic upload (File Upload, with `FileFormat`) | – | ✅ | ✅ | – | ✅ |
| `FDL` | Generic download (File Download, with `FileFormat`) | – | ✅ | ✅ | – | ✅ |
| `BTU` | Generic upload (Business Transaction Upload, carries BTF) | – | – | – | ✅ | ✅ |
| `BTD` | Generic download (Business Transaction Download, carries BTF) | – | – | – | ✅ | ✅ |

## Payments — upload

Business SEPA uploads (#39). Submission via H005 `BTU`+BTF, H003/H004 `FUL`+`FileFormat`, or the direct
code. See [payment orders](payment-orders.md).

| OrderType | Description | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `CCT` | SEPA Credit Transfer | `SCT` / `pain.001` | ✅ | ✅ | ✅ | ✅ |
| `CIP` | SEPA Instant Credit Transfer | `SCT` `INST` / `pain.001` | ✅ | ✅ | ✅ | ✅ |
| `CDD` | SEPA Direct Debit (CORE) | `SDD` `COR` / `pain.008` | ✅ | ✅ | ✅ | ✅ |
| `CDB` | SEPA Direct Debit (B2B) | `SDD` `B2B` / `pain.008` | ✅ | ✅ | ✅ | ✅ |

## Account statements & reports — download

Server-generated, synthetic statements/reports (#40). Requested via H005 `BTD`+BTF, H003/H004
`FDL`+`FileFormat`, or the direct code. See [account statement orders](statement-orders.md).

| OrderType | Description | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `STA` | Account statement (SWIFT MT940) | `EOP` / `mt940` | ✅ | ✅ | ✅ | ✅ |
| `VMK` | Pre-advised items / interim (SWIFT MT942) | `STM` / `mt942` | ✅ | ✅ | ✅ | ✅ |
| `C53` | Bank-to-Customer Statement (camt.053) | `EOP` / `camt.053` | ✅ | ✅ | ✅ | ✅ |
| `C52` | Bank-to-Customer Account Report (camt.052) | `STM` / `camt.052` | ✅ | ✅ | ✅ | ✅ |
| `C54` | Debit/Credit Notification (camt.054) | `EOP` / `camt.054` | ✅ | ✅ | ✅ | ✅ |
| `Z53` | Account statement Swiss/ISO-CH (camt.053) | – | – | ❌ | ❌ | ❌ |

`Z53` is mentioned in the #40 roadmap but **not implemented** (open).

## Status & protocol orders — download

Administrative/technical download orders (#41). Remain `AdminOrderType`s in H005 (no BTF). See
[status & protocol orders](status-protocol-orders.md).

| OrderType | Description | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `HTD` | Subscriber data (incl. address/accounts) | – | ✅ | ✅ | ✅ | ✅ |
| `HKD` | Customer data (incl. all subscribers) | – | ✅ | ✅ | ✅ | ✅ |
| `HAA` | Available order types | – | ✅ | ✅ | ✅ | ✅ |
| `HPD` | Bank parameters | – | ✅ | ✅ | ✅ | ✅ |
| `HAC` | Customer protocol (XML), projection over the event log | – | ✅ | ✅ | ✅ | 🟡 |
| `PTK` | Customer protocol (text), projection over the event log | – | ✅ | ✅ | ✅ | 🟡 |

`HAC`/`PTK` are functionally present and tested, but realised as an in-house projection instead of a
spec-accurate camt.086/pain.002 (see [Open gaps](#open-gaps)).

## Distributed electronic signature (VEU / EDS)

Order types of the distributed electronic signature (#42). Remain `AdminOrderType`s in H005
(no BTF). See [VEU orders](veu-orders.md).

| OrderType | Description | BTF (H005) | H003 | H004 | H005 | Status | Connector |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `HVU` | Overview of open orders (download) | – | ✅ | ✅ | ✅ | ✅ | ✅ `HvuDownloadRequest` |
| `HVZ` | Overview with additional details (download) | – | ✅ | ✅ | ✅ | ✅ | ✅ `HvzDownloadRequest` |
| `HVD` | Status/detail of an order (download) | – | ✅ | ✅ | ✅ | ✅ | ✅ `HvdDownloadRequest` |
| `HVT` | Transaction details of an order (download) | – | ✅ | ✅ | ✅ | 🟡 | ✅ `HvtDownloadRequest` |
| `HVE` | Add signature (upload) | – | ✅ | ✅ | ✅ | ✅ | ✅ `HveUploadRequest` |
| `HVS` | Cancel/reject order (upload) | – | ✅ | ✅ | ✅ | ✅ | ✅ `HvsUploadRequest` |

`HVT` delivers the detail overview order-summary style (no ISO-20022 per-transaction decomposition,
see [Open gaps](#open-gaps)).

The **Connector** column was added with **#124**. Until then the VEU was complete server-side
(since #42) but **not drivable from the bundled client in any version** — the matrix described only the
server and thereby presented a coverage that did not exist from a user's perspective. Details and park
trigger: [Connector: VEU](../connector/veu.md),
[ADR-0030](../adr/0030-transport-defaults-and-client-side-veu.md).

## Schema binding only (not wired)

Order types for which generated bindings exist but which **no** handler processes.

| OrderType | Description | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `HEV` | Query host EBICS versions | – | ❌ | ❌ | ❌ | ❌ |
| `H3K` | Combined key transmission (INI + HIA + certificates) | – | – | ❌ | ❌ | ❌ |

## Open gaps

Consolidated list of the deliberately not-yet-covered items (the "gaps" to be flagged from #43).

- **Z53 (Swiss/ISO-CH account statement).** Mentioned in the [#40](statement-orders.md) roadmap, not
  implemented.
- **PSR / pain.002 download not reachable.** The payment status report from #39 is stored internally
  under the placeholder code `PSR` (`EbicoServerOptions.PaymentStatusReportOrderType`), but **no**
  order type/BTF maps onto this queue — observable only via the Admin API (see
  [payment orders](payment-orders.md)).
- **`SignatureFlag` (per-BTF ES requirement) unchecked.** Whether a `BTU` order requires an ES is not
  evaluated (separate from the plain order-type authorisation).
- **ES/X002 verification deferred (M4).** Payloads are decrypted but not authenticated;
  download responses are unsigned.
- **camt fixed at `.001.08`.** The classic DK profile `.02` is not implemented; no real
  ISO-20022 XSD validation (structural only).
- **HAC/PTK wire format.** In-house projection instead of a spec-accurate camt.086 (HAC) or pain.002 (PTK).
- **HVT order-summary style.** No ISO-20022 per-transaction decomposition.
- **BTF container not round-trip-capable.** The `SVC`/`XML`/`ZIP` value sits best-effort in the binding
  (see [BTF framework](btf-framework.md)).
- **HEV / H3K schema binding only.** No handler, no use in server/connector.
- **BTF catalog is a best-effort seed.** Only the common payment/statement orders are verified against
  the proprietary *BTF mapping / External Code List*; large parts of the EBICS BTF palette are
  not modelled.
- **Conformance against real clients / negative security cases.** Milestone **M8** — **completed**
  (Epic #56). The **E2E happy paths** Connector ↔ Server (INI/HIA/HPB, CCT, C53 — each H003/H004/H005,
  **#57**) demonstrate the consistency of both EBICO sides; **#58** adds the server-side
  X002 verification + wire tampering negative suite; **#59** checks against **real third-party clients**
  ([conformance against real clients](../development/conformance-real-clients.md)). The deviations
  uncovered there (`xsi:type` on `OrderDetails`, misclassification as `061099`, `A006`/PSS on H004,
  modulus with an ASN.1 sign byte) are fixed with **#117**
  ([ADR-0029](../adr/0029-interop-fixes-real-clients.md)); the vendor replay now drives a real client's
  onboarding chain through to `Ready`. Spec conformance against the official annexes generally
  remains only partially verified (schemas proprietary).

## EBICS version mapping

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Order type | `OrderDetails/OrderType` (classic code, e.g. `CCT`, `STA`, `FUL`, `FDL`) | `OrderDetails/AdminOrderType` (`BTU`/`BTD` for business orders, classic admin code otherwise) |
| Business identity | in the OrderType or `FULOrderParams`/`FDLOrderParams` `FileFormat` | in the **BTF** (`BTUOrderParams`/`BTDOrderParams` → `Service`) |
| Admin/VEU orders | classic code | deliberately still `AdminOrderType` (no BTF) |

BTF is purely **H005**; H003/H004 carry no BTF service. The BTF ↔ classic-code resolution is handled by
`BtfOrderTypeCatalog` (details in the [BTF framework](btf-framework.md)).

## Tests

`tests/EBICO.Tests/Docs/OrderCoverageMatrixTests.cs` (xUnit v3 + AwesomeAssertions):

- **Completeness guard** — every order type registered in the code (from `BtfOrderTypeCatalog.All`, the
  four `*OrderTypes` catalogs, and the handler/engine constants) **must** appear in this matrix;
  prevents silent drift between code and docs. Conversely, the matrix may additionally list
  planned/open codes (e.g. `Z53`, `HEV`, `H3K`).
- **Structure guard** — the mandatory sections (`## Legend`, `## Open gaps`, `## EBICS version mapping`)
  are present.

The *business* order tests per family live in the respective feature docs (see below).

## Related documentation

- [BTF framework (H005)](btf-framework.md) — BTF model and `BtfOrderTypeCatalog`
- [Payment orders (CCT/CDD/CDB/CIP)](payment-orders.md)
- [Account statement orders (STA/VMK/C53/C52/C54)](statement-orders.md)
- [Status & protocol orders (HAC/HAA/HTD/HKD/HPD/PTK)](status-protocol-orders.md)
- [VEU orders (HVU/HVZ/HVD/HVT/HVE/HVS)](veu-orders.md)
- [INI](ini.md) / [HIA](hia.md) / [HPB](hpb.md) / [HCA/HCS/SPR/HSA](hca-hcs-spr-hsa.md) — key management
- [Ticket overview](../ticket-overview.md) — milestones and issues (M5: #38–#43)
- [ADR-0016 (BTF framework & authorisation)](../adr/0016-btf-framework-and-authorisation.md)
- [Licence & repo policy](../legal/ebics-licensing.md) — proprietary External Code List
