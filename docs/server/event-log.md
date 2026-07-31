# Server: Event/log store (IEventLog)

> Implementation of **Issue #69** (Milestone M4 — Server: Transaction Engine). This page describes the
> shared, **append-only event/log store** of the emulator: the event model, the
> store abstraction, the query API, the wired write points and the two views read over it.
>
> Deliberately **included**: the event model (`EbicsEvent` + enums `EbicsEventType`/`EbicsEventSeverity`/
> `EbicsEventVisibility`), the abstraction `IEventLog` (Append + Query), the thread-safe in-memory default
> `InMemoryEventLog` (ring buffer via `EbicoServerOptions.MaxEventLogEntries`), the filter API
> `EbicsEventQuery` (customer/period/type/visibility/limit), the **central** write point in the
> [`EbicsRequestPipeline`](host.md) (one event per request) and the **lifecycle** write points in the
> transaction engines ([upload](upload-transaction.md)/[download](download-transaction.md)).
> Deliberately **not yet**: the two **projections** themselves — **HAC** (Customer Protocol, M5) and the
> **Suite inspector** (M7) — consume `IEventLog` only, but are separate issues; a **persistent**
> implementation (SQLite or similar, [ADR-0015](../adr/0015-event-log-store.md)); events from
> the **signature check** (the verify stage is a no-op). VEU events
> (`VeuPending`/`VeuSigned`/`VeuReleased`/`VeuCancelled`) exist since #42 (see [VEU orders](veu-orders.md)).

## Purpose

HAC and the Suite inspector read the **same** event stream, but do not produce it. Without a
shared source, HAC would have nothing to return and the Suite nothing to display — and each view would
otherwise build its own, divergent log. `IEventLog` is this single append-only store: all
server components **write** relevant events into it, nobody mutates or deletes them, and both
views are pure **projections** over it. This foundation belongs before M5 (HAC) and M7 (inspector).

## Model

`EbicsEvent` (`EBICO.Server.State`) is an immutable `sealed record`. `Sequence` and `Timestamp` are
assigned by the store on append — a writer supplies only the functional content.

| Field | Type | Meaning |
| --- | --- | --- |
| `Sequence` | `long` | Monotone, 1-based order; assigned by the store. Stable total ordering, even with the same `Timestamp`. |
| `Timestamp` | `DateTimeOffset` | Time of the append; stamped from the injected `TimeProvider`. |
| `Type` | `EbicsEventType` | Event kind (see below). |
| `Severity` | `EbicsEventSeverity` | `Info` \| `Warning` \| `Error`. |
| `Visibility` | `EbicsEventVisibility` | `CustomerVisible` \| `Internal` — separates the two projections. |
| `HostId` / `PartnerId` / `UserId` | `HostId?` / `PartnerId?` / `UserId?` | Customer/subscriber (from `EBICO.Core.Domain`); nullable, because not every event is fully attributable. |
| `OrderType` | `string?` | Order type (e.g. `HPB`, `BTU`). |
| `TransactionId` | `string?` | Hex transaction ID for transaction events. |
| `ReturnCode` | `EbicsReturnCode?` | Result; carries `Code` + `SymbolicName` + `Kind` ([return-code catalog](../protocol/return-codes.md)). |
| `Message` | `string` | Short, human-readable description. |

**`EbicsEventType`** (focused starting set, extensible): `RequestReceived` (central, per request),
`UploadStarted`/`UploadCompleted`, `DownloadStarted`/`DownloadCompleted`, `ReceiptNegative`,
`TransactionEvicted`.

## Visibility & severity

**Visibility** controls which view sees an event:

- `CustomerVisible` — relevant to the customer, delivered by **HAC** (and also visible in the Suite):
  order received (initialisation/single-phase order), upload/download started/completed, negative
  acknowledgement.
- `Internal` — for the operator only, **only in the Suite inspector**: per-segment transfer/receipt steps
  (protocol noise), eviction of orphaned transactions, internal/technical errors.

The pipeline derives the values per request automatically: `EBICS_OK` → `Info`; `EBICS_INTERNAL_ERROR` →
`Error` + `Internal` (never customer-visible); other rejections → `Warning`. A `RequestReceived` of a
**Transfer/Receipt** phase is `Internal` (segment noise), an initialisation or single-phase order
is `CustomerVisible`.

## Query API

`IEventLog.QueryAsync(EbicsEventQuery, …)` returns the matches **ascending** by `Sequence`. All
filters are optional (`null` = no filter) and combine with **AND**:

| Filter | Effect |
| --- | --- |
| `HostId` / `PartnerId` / `UserId` | Only events of this host/customer/subscriber. |
| `Type` | Only events of this type. |
| `Visibility` | Only events of this visibility (HAC uses `CustomerVisible`). |
| `From` / `To` | Time window: `From` **inclusive**, `To` **exclusive**. |
| `Limit` | At most N (the earliest by `Sequence`); `null`/≤0 = unlimited. |

## Write points

**Central (one event per request):** the [`EbicsRequestPipeline`](host.md) writes, after
processing each request, a `RequestReceived` with subscriber (from the static header —
transfer/receipt carry only the HostID), `OrderType`, `TransactionId` and the final `ReturnCode`. This
also covers the **key management** (INI/HIA/HPB/HCA/HCS/SPR/HSA).

**Lifecycle (semantic transaction events):** the engines write with the full
subscriber binding of the transaction:

- [Upload](upload-transaction.md): `UploadStarted` (initialisation) and `UploadCompleted` (last segment
  reassembled).
- [Download](download-transaction.md): `DownloadStarted` (initialisation), `DownloadCompleted` (positive
  acknowledgement `011000`), `ReceiptNegative` (negative acknowledgement `011001`, data re-enqueued).
- Both: `TransactionEvicted` on the idle-timeout sweep of the
  [`TransactionCleanupService`](transaction-recovery.md) (`Internal`).
- Order processing: `OrderAccepted`/`OrderRejected` (payments, #39) as well as
  `VeuPending`/`VeuSigned`/`VeuReleased`/`VeuCancelled` ([distributed electronic signature](veu-orders.md), #42).

> **Spec caveat:** events from the ES check are deliberately missing — the verify stage is a no-op. The
> distributed electronic signature (VEU, #42), by contrast, writes its own events (see above).

## Two projections

- **HAC (Customer Protocol, M5):** reads per customer and only customer-visible —
  `QueryAsync(new EbicsEventQuery { PartnerId = …, Visibility = CustomerVisible })` — and maps the
  result spec-compliantly. Produces **no** log of its own.
- **Suite inspector (M7):** reads **raw and global** across all customers (without a visibility filter), with
  live filters (customer/period/type/severity) and jump event → transaction. Also sees the internal
  details. The Suite accesses the store in-process ([ADR-0009](../adr/0009-blazor-render-mode.md)).
  **Implemented in [#54](../suite/transaction-inspector.md)**; the raw XML per transaction phase comes
  additionally from the [message-capture store](../adr/0021-message-capture-store.md).

## Example events

| Seq | Type | Severity | Visibility | Partner/User | OrderType | ReturnCode | Message |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `RequestReceived` | Info | CustomerVisible | PARTNER01 / USER01 | INI | `EBICS_OK` | `INI → EBICS_OK` |
| 2 | `RequestReceived` | Info | CustomerVisible | PARTNER01 / USER01 | BTU | `EBICS_OK` | `BTU → EBICS_OK` |
| 3 | `UploadStarted` | Info | CustomerVisible | PARTNER01 / USER01 | BTU | `EBICS_OK` | Upload started (3 segment(s), …) |
| 4 | `RequestReceived` | Info | Internal | — / — (HostID only) | — | `EBICS_OK` | `request → EBICS_OK` (Transfer) |
| 5 | `UploadCompleted` | Info | CustomerVisible | PARTNER01 / USER01 | BTU | `EBICS_OK` | Upload completed (…) |
| 6 | `RequestReceived` | Warning | CustomerVisible | PARTNER02 / USER09 | XYZ | `EBICS_UNSUPPORTED_ORDER_TYPE` | `XYZ → EBICS_UNSUPPORTED_ORDER_TYPE` |
| 7 | `TransactionEvicted` | Warning | Internal | PARTNER03 / USER02 | BTD | — | Download transaction evicted after idle timeout … |

HAC (for PARTNER01) would see only sequences 1, 2, 3, 5; the inspector would see all.

## Configuration

`EbicoServerOptions.MaxEventLogEntries` (default `10000`) bounds the in-memory log: on reaching the
upper limit a new append discards the oldest event (ring buffer). `0` = unlimited (grows until the
process restarts). The sequence numbers keep growing independently of the eviction.

## Persistence

The default `InMemoryEventLog` retains nothing beyond a process restart — the same approach as the
rest of the server state ([ADR-0011](../adr/0011-server-master-data-management.md)). The interface is
**asynchronous**, so that a persistent store (SQLite or similar) can later be plugged in via
`TryAddSingleton<IEventLog, …>` before `AddEbicoServer`, **without** changing a caller.
Details and scope: [ADR-0015](../adr/0015-event-log-store.md).
