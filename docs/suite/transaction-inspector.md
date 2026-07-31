# Suite: Transaction Inspector

> Implementation of **Issue #54** (Milestone M7 — Suite). Builds on the UI shell
> ([#52](ui-shell.md)) and is the operator/developer view over the server-side
> **event/audit log** ([#69](../server/event-log.md)) and the transaction stores
> from `EBICO.Server`: it reads them **in-process** per [ADR-0009](../adr/0009-blazor-render-mode.md).
> The raw-XML view is fed by a new **message-capture store**
> ([ADR-0021](../adr/0021-message-capture-store.md)).

## Purpose

The `/transactions` page shows the emulator's upload/download transactions — ongoing as well as
completed — and per transaction the **raw XML** (request/response per phase), the **decrypted
OrderData** and the **return codes**. Below it lies the **global log view**: the unfiltered
event stream across **all** customers (including operator-internal events), with live filters and
a jump from an event to its associated transaction.

It is the second projection over the `IEventLog` (the first is the customer-facing HAC order, M5) —
unlike HAC, the inspector reads **raw and global**, without a visibility filter.

## Binding: in-process instead of HTTP

The Suite is a **standalone process** (it hosts no EBICS pipeline). As with the
master data, it registers the required `EBICO.Server` stores directly and reads them via a
read-model provider — the in-process binding envisaged by [ADR-0009](../adr/0009-blazor-render-mode.md).
Since no live traffic arises, sample transactions are seeded.

```csharp
// Program.cs — transaction/event state in-process (ADR-0009)
builder.Services.AddOptions<EbicoServerOptions>();
builder.Services.AddSingleton(TimeProvider.System);              // required by the event log / capture store
builder.Services.AddSingleton<IEventLog, InMemoryEventLog>();
builder.Services.AddSingleton<IUploadTransactionStore, InMemoryUploadTransactionStore>();
builder.Services.AddSingleton<IDownloadTransactionStore, InMemoryDownloadTransactionStore>();
builder.Services.AddSingleton<IMessageCaptureStore, InMemoryMessageCaptureStore>();
builder.Services.AddScoped<ITransactionInspectorProvider, TransactionInspectorProvider>();
…
var app = builder.Build();
await TransactionInspectorSeeder.SeedAsync(app.Services);        // sample transactions/events/captures
```

| Type | Role |
| --- | --- |
| `IEventLog` (Server) | Event stream; **source of the transaction list** (completed transactions leave the stores) and of the global log view |
| `IUploadTransactionStore` / `IDownloadTransactionStore` | Enrichment of the resident transaction: segment count and **decrypted OrderData** (`UploadTransaction.OrderData` / `DownloadTransaction.OrderDataPlaintext`) |
| `IMessageCaptureStore` (Server) | Raw XML (request/response per phase), keyed by transaction ID ([ADR-0021](../adr/0021-message-capture-store.md)) |
| `TransactionInspectorProvider` | Read model: assembles event log, stores and captures into UI DTOs |
| `TransactionInspectorSeeder` | fills the (empty) in-memory stores at startup with sample transactions |

> **Limit:** In this standalone form the inspector shows seeded data. Real,
> cross-process live inspection requires a persistent, shared store
> (SQLite or similar, [ADR-0015](../adr/0015-event-log-store.md)) — a follow-up topic.

## Render mode

The page itself is **Static SSR**; the inspector is **one** interactive island
(`<TransactionInspector @rendermode="InteractiveServer" />`, ADR-0009). The entire state
(selected transaction, active tab, filters) lives in this one island/circuit, so that the jump
"event → transaction" works without cross-island communication.

## Structure

| Area | Content |
| --- | --- |
| Transaction list (`#tx-list`) | Status badge (ongoing/completed/failed/evicted), direction, OrderType, customer/subscriber, segments, ID, last return code, "details" |
| Detail view (`#tx-detail`) | Tabs **raw XML** (request/response per phase, `#tab-rawxml`), **OrderData** (text/hex, `#tab-orderdata`), **event history** (`#tab-events`) |
| Global log (`#event-log`) | unfiltered event list across all customers with live filters and a jump to the transaction |

## Transaction list & status

The list is **reconstructed from the event log** (grouped by `TransactionId`), because
completed transactions leave the stores after the idle timeout. The status is derived from the
event types: `TransactionEvicted` → **evicted**; `Upload/DownloadCompleted` →
**completed**; a rejection (severity ≥ Warning or negative acknowledgement) → **failed**;
otherwise **ongoing**. As long as the transaction is **resident**, the provider adds segment count and
decrypted OrderData from the respective store.

## Raw XML & OrderData

- **Raw XML** comes from the `IMessageCaptureStore`: one request/response pair per transaction phase,
  displayed in `<pre>` blocks **HTML-escaped** (Blazor `@` interpolation, no `MarkupString` →
  no XSS). Oversized documents are truncated server-side (hint in the UI).
- **OrderData** is already **decrypted and decompressed** (no Base64): a plain
  document byte stream (pain.001/camt/MT). A text/binary heuristic decides text vs.
  hex representation; the full byte length is shown. Transactions no longer resident (or not yet
  completed) have no OrderData.

## Global log view & filters

The event list reads `IEventLog.QueryAsync` **without** a visibility filter (including `Internal` noise).
Live filters: **customer** (partner dropdown), **type** (`EbicsEventType`), **severity** and **time range**
(`Von`/`Bis`, UTC). Customer/type/time range are passed through to `EbicsEventQuery`; **severity is
filtered client-side**, since `EbicsEventQuery` carries no severity dimension. Every event row
with a transaction ID has a "→ transaction" jump that opens the detail view.

**The selection lists are data-driven** (`GetCustomerOptionsAsync` / `GetTypeOptionsAsync` /
`GetSeverityOptionsAsync`): only what actually occurs in the log is offered. Previously, type
and severity came from `Enum.GetValues`, so that most options (`VeuSigned`, `ReceiptNegative`,
`Error`, …) reliably led to "no events for the current filter." (#126). "Refresh"
re-reads the lists and **discards a selection that no longer occurs** — the table thus cannot
get stuck on an empty result.

## Limits

- **Key-management orders** (INI/HIA/HPB/…) carry no transaction ID and are therefore **not**
  captured raw — they still appear in the global log, just without a raw-XML tab.
- No cross-process live state (see above, ADR-0015).

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; bUnit for the UI):

- `Server/InMemoryMessageCaptureStoreTests` — sequence/timestamp, ring buffer, keyed lookup, truncation.
- `Server/MessageCaptureWritePointTests` — the pipeline captures init+transfer raw; INI (without TxId) not.
- `Suite/TransactionInspectorProviderTests` — reconstruction/status/kind, filters (incl. severity
  client-side), OrderData resident vs. `null`, customer/type/severity options (only what occurs,
  empty log → empty lists).
- `Suite/TransactionInspectorTests` — bUnit: list + status badges, detail tabs (raw XML/OrderData),
  live severity filter, jump event→transaction, data-driven filter options.
- `Suite/TransactionInspectorSeederTests` — the seeder fills log/stores/captures and is idempotent.

## Related

- [UI shell & navigation](ui-shell.md)
- [Server: event/audit log (#69)](../server/event-log.md) — the shared event source
- [Server: host & pipeline](../server/host.md) — the capture write point in the `EbicsRequestPipeline`
- [ADR-0009 — Blazor render mode (in-process state)](../adr/0009-blazor-render-mode.md)
- [ADR-0015 — Event/audit log](../adr/0015-event-log-store.md)
- [ADR-0021 — Message-capture store](../adr/0021-message-capture-store.md)
