# 0015 — Event/audit log store (`IEventLog`)

- Status: accepted
- Date: 2026-07-14

## Context

Two planned features read the same server-side event stream but do not produce it:
the customer-side **HAC** protocol order (M5) and the **Suite inspector** (M7).
Without a shared source, HAC would have nothing to return and the Suite nothing to
display; each view would otherwise build up its own, diverging log. What is needed is
**one** append-only store into which all server components write relevant events
(order received, return code assigned, transaction completed, key-mgmt action …),
with enough structure for both projections.

To be decided: (a) the modelling of the event and the store abstraction, (b) the
persistence approach, and (c) where events are written.

## Decision

1. **One append-only event model** `EbicsEvent` (immutable `sealed record`,
   ADR-0007 style) with: a store-assigned monotonic `Sequence` + stamped `Timestamp`,
   `Type`/`Severity`/`Visibility` (enums), nullable subscriber coordinates
   (`HostId`/`PartnerId`/`UserId` from `EBICO.Core.Domain`), `OrderType`,
   `TransactionId` (hex), full `EbicsReturnCode` (ADR-0012, carries `Code`+`SymbolicName`)
   and `Message`. **Visibility** (`CustomerVisible` vs. `Internal`) is the field on
   which the two projections separate.
2. **`IEventLog` = append + query**, asynchronous, pluggable via `TryAddSingleton` —
   **exactly the store path** from [ADR-0011](0011-server-master-data-management.md).
   Query filters by customer/time range/type/visibility (`EbicsEventQuery`, `From`
   inclusive / `To` exclusive, optional `Limit`).
3. **In-memory default (`InMemoryEventLog`), persistence deferred.** This is "the same
   persistence approach as the rest of the server state": in-memory, thread-safe, with
   a **ring buffer** (`EbicoServerOptions.MaxEventLogEntries`, default 10,000) as a
   memory upper bound. The async interface is built so that a persistent store (SQLite
   or similar) later **only replaces the implementation**, without changing callers.
4. **Write points: central + lifecycle.** A **central** point in the
   [`EbicsRequestPipeline`](../server/host.md) writes a `RequestReceived` event per
   request (subscriber/order type/phase/return code) — this also covers key
   management. The transaction engines
   ([#32](0013-upload-transaction-engine.md)/[#33](0014-download-transaction-engine.md))
   add **semantic lifecycle events** (upload/download started/completed, negative
   acknowledgement, eviction in the background sweep), since these span a transaction
   across multiple requests or arise request-less in cleanup.

## Consequences

- HAC and the Suite become pure **projections** over `QueryAsync` — HAC with
  `{ PartnerId, Visibility = CustomerVisible }`, the Suite raw/global. No parallel log
  system.
- The event log is the **first** building block with a real persistence perspective;
  until a SQLite store exists, the log is lost on restart like the rest of the server
  state — acceptable for the emulator. A concrete persistent store is follow-up work
  (see the backlog in the ADR index).
- Segment-wise transfer/receipt steps are marked `Internal` so the HAC view does not
  clog with protocol noise; internal errors (`EBICS_INTERNAL_ERROR`) are never
  customer-visible.
- **VEU/ES and X002 signature verification** are not yet wired up (the verify stage is
  a no-op, VEU does not exist server-side) — corresponding events follow once these
  steps become real.

## Alternatives

- **`ILogger`/structured logging** as the event source. Rejected: logs are for
  humans/sinks, not queryable per customer/time range and carry no
  visibility/return-code semantics that HAC needs.
- **Implement SQLite right away.** Rejected for this PR: it makes the event log the
  only persistent store (asymmetry to the otherwise volatile state) and pulls in a new
  dependency + its own persistence ADR. The async interface keeps the path open
  without going down it now.
- **Granular write points in every key-mgmt handler.** Rejected: the central pipeline
  point already carries subscriber + order type + return code; scattered calls across
  ~15 handlers would be redundant and error-prone.
