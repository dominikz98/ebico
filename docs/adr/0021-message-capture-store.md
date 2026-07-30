# 0021 — Message-capture store (`IMessageCaptureStore`)

- Status: accepted
- Date: 2026-07-16

## Context

The Suite's transaction inspector (M7, [#54](../suite/transaktions-inspektor.md)) is to
display the **raw XML** of request and response per phase for each transaction. This XML
arises only **transiently** during the pipeline run: the request lives in
`EbicsRequestContext.RequestXml`, the response in the serialised response bytes; both are
discarded afterwards. Neither the [`IEventLog`](0015-ereignis-protokollspeicher.md)
(which carries structured events, not envelopes) nor the transaction stores (which carry
decrypted order data, not XML) hold the raw message. A store for the verbatim envelopes
is missing.

To be decided: (a) where the raw XML is held, (b) the model and the store abstraction,
and (c) how the store is bounded.

## Decision

1. **A dedicated, transaction-scoped store** `IMessageCaptureStore` (append +
   get-by-TransactionId), **not** a new field on `EbicsEvent`. Rationale: the event model
   stays lean (the HAC projection would never need the XML), and the captures are purely
   operator-side (only the Suite inspector reads them). The model `CapturedMessage` is an
   immutable `sealed record` (ADR-0007 style) with a store-assigned `Sequence`/`Timestamp`,
   `TransactionIdHex` (key), `Phase`, optional `SegmentNumber`, subscriber coordinates,
   `RequestXml`/`ResponseXml` (as **text**, not `byte[]`) and full `EbicsReturnCode`.
2. **One central write point in the [`EbicsRequestPipeline`](../server/host.md)** — right
   after the serialisation of the response, the only point where request XML, response
   XML, the resolved transaction ID/phase and the final return code are present
   simultaneously. Capture happens **only** when a transaction ID is resolvable.
   **Key-management orders** (INI/HIA/HPB) carry no transaction ID and are deliberately
   **not** captured — they still appear in the event log, just without raw XML.
3. **In-memory default (`InMemoryMessageCaptureStore`), pluggable via `TryAddSingleton`**
   — exactly the store path from
   [ADR-0011](0011-server-stammdatenverwaltung.md)/[ADR-0015](0015-ereignis-protokollspeicher.md).
   Memory bounding on two axes: a **ring buffer** across all captures
   (`EbicoServerOptions.MaxMessageCaptureEntries`, default 200) and **truncation** per XML
   document (`EbicoServerOptions.MaxCapturedMessageBytes`, default 256 KiB). The async
   interface keeps the path to a persistent store (SQLite or similar) open without
   changing any caller.

## Consequences

- The inspector gets the raw XML as a pure **projection** via `GetAsync(transactionIdHex)`;
  no second log system arises alongside the event log, only a transaction-scoped
  appendix.
- Truncation is a pure **display** shortening (with a `*Truncated` flag); the
  authoritative, decrypted order data comes from the transaction store, not from a
  truncated capture.
- Raw XML for key management is thereby **not** covered — if wanted later, it needs its
  own (non-transaction-scoped) key; deliberately deferred.
- Like the rest of the server state, the in-memory store is lost on restart; a persistent
  store is follow-up work (the same backlog item as for the `IEventLog`, ADR-0015).

## Alternatives

- **Raw XML as fields on `EbicsEvent`.** Rejected: bloats every event (including the many
  without a transaction reference), burdens the HAC projection and mixes structured events
  with envelopes.
- **Raw XML on the transaction (`UploadTransaction`/`DownloadTransaction`).** Rejected: the
  engines do not see the envelope XML (only the pipeline does), and the transaction objects
  are evicted after the idle timeout — the raw XML should outlive the transaction (up to
  the ring-buffer limit).
- **No persistent store, just "pass through" the raw XML at runtime.** Rejected: the
  inspector reads asynchronously, long after the request; without a store there would be
  nothing to display.
