# 0013 — Upload transaction engine & store

- Status: accepted
- Date: 2026-07-13

## Context

Until M4 the server processed EBICS requests **single-shot**: the
[`EbicsRequestPipeline`](../server/host.md) resolves exactly **one** handler per
request via the `(Version, OrderType)` resolver. The EBICS **upload** (issue #32),
by contrast, is the first **multi-phase** transaction: an **initialisation**
(transaction ID assignment, state build-up) and a sequence of **transfer** messages
(segment-wise order data). This collides with the existing dispatch, because a
transfer request carries **no** order type — only the `TransactionID` — and multiple
messages share **common state** (transaction key, segment buffer, subscriber binding,
phase).

To be decided: (a) how the transaction phases dock onto the pipeline, and (b)
where/how the cross-transaction state is held.

## Decision

1. **Dedicated engine for both phases** instead of splitting across a resolver
   handler (init) and a separate transfer path: `IUploadTransactionEngine`/
   `UploadTransactionEngine` owns **init and transfer** and encapsulates the state
   machine. It has its own result type (`UploadTransactionResult`), so the handler
   contract (`EbicsOrderResult`) and the single-shot handlers
   (INI/HIA/HPB/HCA/HCS/SPR/HSA) stay untouched.
2. **Phase routing in the pipeline before the resolver:** a signed `ebicsRequest`
   with a `TransactionID` (i.e. `phase=Transfer`) goes to `ContinueUploadAsync`; an
   `ebicsRequest` with `phase=Initialisation` and order type **FUL** (H003/H004) or
   **BTU** (H005) goes to `BeginUploadAsync`. Everything else falls back unchanged to
   the `(Version, OrderType)` resolver — HCA/HCS/SPR (also signed `ebicsRequest`)
   stay single-shot.
3. **In-memory transaction store** `IUploadTransactionStore` (default
   `InMemoryUploadTransactionStore`), analogous to the master-data store from
   [ADR-0011](0011-server-stammdatenverwaltung.md): thread-safe, pluggable via
   `TryAddSingleton`, **keyed on `Convert.ToHexString(TransactionID)`** (a `byte[]` is
   unsuitable as a dictionary key).
4. **Transaction/segment errors as control flow** (returned directly as a return
   code), not as exceptions; only the decode errors (decryption/decompression) run
   via `OrderDataFault` → `EbicsErrorMapper` (`090004`). **No** new return codes were
   needed — the catalogue from [ADR-0012](0012-returncode-katalog.md) already contains
   them.

## Consequences

- The resolver dispatch stays simple and unchanged for the key-management handlers;
  the transaction logic is bundled in **one** place (high cohesion, no implicit shared
  state across two classes).
- New order types with upload semantics can later be attached to the same engine
  (`IsUploadOrderType`) without touching the pipeline again.
- The in-memory store keeps orphaned (aborted after init) and completed transactions
  until restart — **eviction/TTL/recovery is issue #35**. Acceptable for the emulator.
- **ES verification** is deliberately deferred (order data decrypted, not
  authenticated) — consistent with HCA/HCS; details and follow-up work in
  [docs/server/upload-transaction.md](../server/upload-transaction.md).

## Alternatives

- **Init via the resolver, transfer via a special path.** Rejected: the shared
  transaction state would have implicitly coupled two classes, and `EbicsOrderResult`
  would have had to be extended with transaction fields.
- **Generic interceptor for every upload `ebicsRequest`** (instead of the fixed
  FUL/BTU binding). Rejected: it would have to distinguish an init from the
  single-phase HCA/HCS/SPR — error-prone; the order-type whitelist is unambiguous.
