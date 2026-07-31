# 0019 — Status & protocol orders (domain extension, HAC/PTK as IEventLog projection)

- Status: accepted
- Date: 2026-07-15

## Context

Issue #41 (Milestone M5) requires the **administrative/technical order types**: HTD
(subscriber data), HKD (customer data), HAA (available order types), HPD (bank
parameters) as well as HAC/PTK (customer protocol, machine- and human-readable
respectively). These stay **AdminOrderTypes** in H005 (no BTF service, see
[ADR-0016](0016-btf-framework-and-authorisation.md)) and are bank→client **downloads**.

Already present: the generic [download transaction](../server/download-transaction.md)
(#33), the generate-on-demand pattern `IDownloadOrderProcessor` (#40,
[ADR-0018](0018-account-statement-download-orders.md)) and the append-only
[`IEventLog`](../server/event-log.md) (#69, [ADR-0015](0015-event-log-store.md)),
which is explicitly intended as the source for the HAC projection. Also present:
generated bindings `HTD/HKD/HAA/HPDResponseOrderData` per version — but **not** for
HAC/PTK (proprietary/no schema).

To be decided: (1) where the master data for HTD/HKD/HPD comes from, (2) whether these
orders require an explicit authorisation, (3) in which format HAC is delivered, (4) how
the routing/generation is wired up.

## Decision

1. **Extend the domain model** instead of populating it synthetically: new value types
   `Address` and `BankAccount` in `EBICO.Core.Domain`; `Partner` now carries `Address?`
   + `IReadOnlyCollection<BankAccount>`, `Bank` an optional `Url` (HPD access,
   `Institute`=`Name`), `Subscriber` an optional `Name`. The admin API
   (`MapEbicoAdminApi`) and its DTOs were extended accordingly; `Name` is threaded
   through all immutable subscriber copy operations
   (`Transition`/`WithPermission(s)`/`WithoutPermissionsFor`).

2. **Authorisation required**: the orders run unchanged through the strict
   `HasPermissionFor` gate of the download engine (permission missing → `090003`). No
   auto-grant, no exception for admin orders — consistent with the BTF orders (ADR-0016).
   The subscriber is authorised for HTD/HAC/… via the admin API.

3. **HAC as its own, spec-plausible XML projection** over `IEventLog`
   (`QueryAsync { PartnerId, Visibility = CustomerVisible }`, optionally date-range
   filtered): an `HACResponseOrderData` with one `ProtocolEntry` per customer-visible
   event (hand-built like the camt/pain builders). **PTK** renders the same projection
   as text. The generation itself is logged only as an `Internal` event (not as an
   additional customer-visible `OrderAccepted`); the `DownloadStarted`/`DownloadCompleted`
   lifecycle events of the transaction stay — as with every download — customer-visible,
   so a protocol retrieval is itself visible in later protocols.

4. **Wiring as a download** via two new `IDownloadOrderProcessor`:
   `SubscriberInfoDownloadProcessor` (HTD/HKD/HAA/HPD, from the `IMasterDataManager` via
   `SubscriberInfoContentBuilder`) and `CustomerProtocolDownloadProcessor` (HAC/PTK).
   `DownloadTransactionEngine.IsDownloadOrderType` additionally recognises the codes
   (`StatusProtocolOrderTypes`); the engine now takes
   `IEnumerable<IDownloadOrderProcessor>` and picks the first matching `CanProcess`
   (instead of exactly one processor).

## Consequences

- The emulator answers all six orders across all three versions; HTD/HKD/HAA/HPD are
  testable via round-trip against the bindings, HAC/PTK against the projected events —
  without proprietary fixtures.
- The switch to multiple `IDownloadOrderProcessor` is additive (the statement processor
  #40 stays registered); third-party processors can still be added via `AddSingleton`.
- **Spec caveats:** HAC/PTK are plausible in-house formats (the official
  camt.086/pain.002 layout is not verified); the version-specific HTD/HKD/HAA/HPD field
  mapping omits unmodelled fields (order/transfer format, amount limits, authorisation
  level, X.509 parameters, account usage restrictions). The EBICS user `Status` is
  heuristically derived from the lifecycle.

## Alternatives

- **Synthetic master data** (no domain model) — faster, but HTD/HKD/HPD would stay
  content-empty; rejected in favour of real accounts/addresses/bank parameters
  maintainable via the admin API/Suite.
- **Admin orders without authorisation** (for every `Ready` subscriber) — more
  convenient, but deviates from the strict authorisation model (ADR-0016); rejected in
  favour of consistency.
- **Base HAC on pain.002** (reuse `PainStatusReportBuilder`) — semantically fitting only
  for order status, not for a complete event protocol; rejected in favour of its own
  projection.
- **A dedicated order handler instead of a download processor** — would have duplicated
  encryption/segmentation; rejected in favour of the existing download transaction.
