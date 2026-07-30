# 0014 — Download transaction engine, store & data provisioning

- Status: accepted
- Date: 2026-07-13

## Context

After the [upload transaction](0013-upload-transaktions-engine.md) (#32), the EBICS
**download** (#33) is the second multi-phase transaction — and the first in the
**send direction** (server→client). It has **three** phases: **initialisation**
(provide data, compress, E002-encrypt, segment, assign transaction ID, first
segment), **transfer** (remaining segments) and **receipt** (client acknowledges).
Compared with the upload, three points were new to decide:

1. **Routing collision upload ↔ download.** A transfer request carries only the
   `TransactionID`, no order type. The pipeline must route it to the **correct**
   engine — the [ADR-0013 rule](0013-upload-transaktions-engine.md) ("`TransactionID`
   present → upload") would wrongly route download transfers to the upload engine. In
   addition, the **receipt** phase is new (upload does not have it).
2. **Origin of the order data** ("data provisioning server-side"). So far there was
   **no** order-data store — only schema bindings.
3. **Effect of the acknowledgement.** A positive vs. negative acknowledgement must
   have an effect (`011000` vs. `011001`) — the question was whether only the return
   code or the data state too is affected.

## Decision

1. **Dedicated engine for all three phases**, analogous to ADR-0013:
   `IDownloadTransactionEngine`/`DownloadTransactionEngine` with `BeginDownloadAsync` /
   `ContinueDownloadAsync` / `AcknowledgeReceiptAsync` and its own result type
   (`DownloadTransactionResult` + `DownloadSegmentPayload`). The upload engine and the
   single-shot handlers stay untouched. Its own store `IDownloadTransactionStore`
   (default `InMemoryDownloadTransactionStore`), thread-safe, pluggable via
   `TryAddSingleton`, **keyed on `Convert.ToHexString(TransactionID)`**.
2. **Routing by store ownership rather than order type** (the core of the decision).
   In the pipeline, before the resolver, in a fixed order:
   - `phase=Receipt` → **always** download (uploads have no receipt phase);
   - transfer / `TransactionID` present → `_downloadEngine.OwnsTransaction(id)`
     decides: hit → download transfer, otherwise fall back to the upload transfer
     (which returns `091101` for a genuinely unknown ID);
   - `phase=Initialisation` → by order type: **FUL/BTU** → upload, **FDL/BTD** →
     download.
   16-byte random IDs make store collisions practically impossible.
3. **Provider abstraction + admin API for data provisioning.** `IDownloadDataProvider`
   (default `InMemoryDownloadDataProvider`) holds a **FIFO queue** of plaintext order
   data per (subscriber, order type); the initialisation takes the next element (empty
   → `090005`). Data is fed in via the existing
   [admin API](0011-server-stammdatenverwaltung.md) (`POST …/downloads/{orderType}`),
   analogous to master-data management. A real data store is swappable via
   `TryAddSingleton`.
4. **Consumption semantics.** The initialisation removes the data. A **positive**
   acknowledgement (`011000`) leaves it consumed; a **negative** one (`011001`)
   re-enqueues it. Both acknowledgement codes are **technical** → header (via
   `EbicsReturnCode.Kind`). **No** new return codes were needed — the catalogue from
   [ADR-0012](0012-returncode-katalog.md) already contains them.

## Consequences

- Upload and download share the pipeline and the response factory (new
  `BuildDownloadResponse` alongside `BuildTransactionResponse`), but stay **separate**
  as engines/stores (high cohesion). The download is the first productive user of
  `EbicsSegmentation.Split` and `EncryptionE002.Encrypt` in one transaction.
- The `OwnsTransaction` routing keeps the store encapsulated (the pipeline talks only
  to engines) and stays unchanged for the key-management handlers.
- Positive/negative acknowledgements are **behaviour-affecting** and thus testable
  (another download after positive → `090005`, after negative → the same data).
  Emulator use without code (admin API only) is possible.
- The in-memory provider/store does not persist and does not evict — orphaned
  transactions (no receipt) keep the removed data "in progress" until restart;
  **eviction/TTL/recovery is #35**. Acceptable for the emulator.
- The response stays **unsigned** (X002 = M4); details in
  [docs/server/download-transaction.md](../server/download-transaction.md).

## Alternatives

- **Extend the upload engine instead of a second engine.** Rejected:
  init/transfer/receipt semantics and state (fully segmented send buffer vs. receive
  segment buffer) differ too much; two focused engines are clearer and mirror ADR-0013.
- **Routing by order type in the transfer too.** Not possible: the transfer request
  carries no order type. A phase-plus-store heuristic is the only reliable
  distinction.
- **Fixed placeholder instead of a provider.** Rejected: the issue point "data
  provisioning server-side" requires a real, seedable source; a fixed echo payload
  would be neither realistic nor testable for the `090005`/consumption paths.
- **Stateless acknowledgement** (return code only, data state untouched). Rejected:
  positive/negative acknowledgement would then differ only in the code, not in effect
  — the consumption semantics link receipt and data provisioning realistically.
