# Server: Download transaction (Initialisation + Transfer + Receipt)

> Implementation of **Issue #33** (Milestone M4 — Server: Transaction Engine). This page
> describes the server-side **send engine** for an EBICS download: the
> three-phase transaction consisting of **Initialisation** (data provisioning, compression,
> E002 encryption, segmentation, transaction-ID assignment, first segment),
> **Transfer** (delivering the remaining segments) and **Receipt** (evaluation of the
> client acknowledgement).
>
> Deliberately **included**: the transaction state machine (`DownloadTransactionEngine`), the
> in-memory transaction store (`IDownloadTransactionStore`), the server-side
> **data provisioning** (`IDownloadDataProvider` + admin API), the compression
> (`EbicsCompression`)/E002 encryption (`EncryptionE002`)/segmentation
> (`EbicsSegmentation.Split`) of the order data, the phase routing in the pipeline (incl.
> upload/download distinction), the **receipt** processing (positive/negative) and the
> triggering of the transaction/acknowledgement return codes. Wired to the generic
> download OrderTypes **FDL** (H003/H004) and **BTD** (H005).
> Deliberately **not yet**: the **signature** of the response (X002 = M4; the response is unsigned);
> **recovery/timeouts** and the eviction of orphaned transactions (#35); a real
> persistent order-data store (the `IDownloadDataProvider` is in-memory, replaceable).
> The **order-type-specific data generation** (FDL `FileFormat`/BTD BTF → resolved order type,
> synthetic account statements/reports with period filter) was delivered later with the
> [download orders (#40)](statement-orders.md).

## Purpose

An EBICS download transfers order data in **three phases**. In the **Initialisation** the
client requests data (order type, subscriber); the server **provides the data**, compresses
and E002-encrypts it for the **subscriber's encryption key** (as with HPB),
**segments** the ciphertext, assigns a **Transaction-ID** and returns `NumSegments` +
segment 1. In the **Transfer** phase the client fetches segments 2…N. In the
**Receipt** phase the client acknowledges receipt (`ReceiptCode` 0 = positive, 1 = negative); the
server completes the post-processing.

#33 mirrors the [upload transaction](upload-transaction.md) (#32) in the opposite direction: it
composes the same policy-free primitives ([segmentation](segmentation.md),
[E002](../protocol/encryption-e002.md), [compression](segmentation.md)) — here in the **send direction**
(`Split`/`Compress`/`Encrypt` instead of `Reassemble`/`Decrypt`/`Decompress`) — into a
state machine. The server→client payload path is the same as with
[HPB](hpb.md), only multi-segment and embedded in a transaction.

## Server-side data provisioning

Where the download data comes from is encapsulated by `IDownloadDataProvider` (`EBICO.Server.State`). The default
`InMemoryDownloadDataProvider` holds per **(subscriber, order type)** a **FIFO queue** of
plaintext order data. A download initialisation **dequeues** (`TryDequeueAsync`) the next
element; if the queue is empty, the engine answers with `090005` (`EBICS_NO_DOWNLOAD_DATA_AVAILABLE`).

**Consumption semantics:** the initialisation dequeues the data immediately. A **positive** acknowledgement
(`011000`) leaves it dequeued (consumed) — a repeated download yields `090005`. A
**negative** acknowledgement (`011001`) **re-enqueues the data** (`EnqueueAsync`), so that it can be
fetched again.

Data is enqueued via the **admin API** (unauthenticated, local/emulator only, like the
[master-data admin API](master-data.md)):

| Method | Path | Effect |
| --- | --- | --- |
| `POST` | `…/subscribers/{userId}/downloads/{orderType}` | Body `{"base64Data":"…"}` → enqueue order data (base64); response `{"pending":n}` |
| `GET` | `…/subscribers/{userId}/downloads/{orderType}` | Number of waiting payloads: `{"pending":n}` |

(full path: `/admin/banks/{hostId}/partners/{partnerId}/subscribers/{userId}/downloads/{orderType}`;
invalid base64 → HTTP 400). A real order-data store can be substituted via `TryAddSingleton` before
`AddEbicoServer`.

## Flow

The server routes the phase in the pipeline **before** the resolver: `phase=Receipt` is download-only;
an `ebicsRequest` with `TransactionID` (or `phase=Transfer`) goes to the download engine, **if**
the ID belongs to a download transaction (`OwnsTransaction`), otherwise to the upload engine; an
`ebicsRequest` with `phase=Initialisation` and order type **FDL/BTD** starts a download.

### Phase 1 — Initialisation

| Step | Action |
| --- | --- |
| 1. Identity | Check `HostID`/`PartnerID`/`UserID`; subscriber must exist and be `Ready` (otherwise `091002`) |
| 2. Enc key | Encryption key (`E00x`) of the subscriber from `IServerKeyStore` (missing → `091002`) |
| 3. Data provisioning | Next order data via `IDownloadDataProvider.TryDequeueAsync` (empty → `090005`, no transaction) |
| 4. Prepare | `EbicsCompression.Compress` → `EncryptionE002.Encrypt` (for the subscriber's enc key) → `PublicKeyFingerprint.Compute` |
| 5. Segment | `EbicsSegmentation.Split(ciphertext, SegmentSizeBytes)`; `NumSegments > MaxDownloadSegments` → `091114` (data is re-enqueued) |
| 6. Create transaction | Generate 16-byte `TransactionID`, state (subscriber, OrderType, segments, enc info, plaintext for re-enqueue) in the `IDownloadTransactionStore` |
| 7. Response | `ebicsResponse`, `phase=Initialisation`, `TransactionID`, `NumSegments`, `SegmentNumber=1`, `DataTransfer` (DataEncryptionInfo + segment 1), `EBICS_OK` |

```xml
<!-- Request (abridged) -->
<ebicsRequest Version="H004" ...>
  <header authenticate="true">
    <static>
      <HostID>EBICOHOST</HostID> <PartnerID>PARTNER01</PartnerID> <UserID>USER01</UserID>
      <OrderDetails><OrderType>FDL</OrderType> ... </OrderDetails>
    </static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase></mutable>
  </header>
  <body/>
</ebicsRequest>

<!-- Response (abridged) -->
<ebicsResponse Version="H004" ...>
  <header><static><TransactionID>…</TransactionID><NumSegments>3</NumSegments></static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase>
      <SegmentNumber lastSegment="false">1</SegmentNumber>
      <ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body><DataTransfer>
    <DataEncryptionInfo authenticate="true">
      <EncryptionPubKeyDigest Version="E002" Algorithm="…sha256">…</EncryptionPubKeyDigest>
      <TransactionKey>…</TransactionKey>              <!-- RSA-OAEP für den Teilnehmer -->
    </DataEncryptionInfo>
    <OrderData>…segment 1…</OrderData>
  </DataTransfer><ReturnCode>000000</ReturnCode></body>
</ebicsResponse>
```

### Phase 2 — Transfer (segments 2…N)

| Step | Action |
| --- | --- |
| 1. Find transaction | `Static/TransactionID` → hex lookup in the store (missing → `091101`) |
| 2. Check segment number | `Mutable/SegmentNumber` in `[1, NumSegments]` (0/missing → `091112`, > N → `091104`) |
| 3. Deliver segment | Segment k from the state; `lastSegment` derived server-side from `k == NumSegments` |
| 4. Response | `ebicsResponse`, `phase=Transfer`, `TransactionID`, `SegmentNumber`, `DataTransfer/OrderData` (**no** DataEncryptionInfo, **no** NumSegments), `EBICS_OK` |

The `DataEncryptionInfo` (transaction key + digest) travels **only** in the init response; the
transfer responses carry only the respective `OrderData` segment. The client reassembles
all segments (init segment 1 + transfer 2…N), decrypts with the once-delivered
transaction key (RSA-OAEP with its private enc key) and decompresses.

### Phase 3 — Receipt (acknowledgement)

| Step | Action |
| --- | --- |
| 1. Find transaction | `Static/TransactionID` → hex lookup (missing → `091101`) |
| 2. Read acknowledgement | `body/TransferReceipt/ReceiptCode` (missing → `091112`) |
| 3. Post-processing | Remove transaction; `ReceiptCode=0` → data stays consumed; otherwise re-enqueue data via provider |
| 4. Response | `ebicsResponse`, `phase=Receipt`, `TransactionID`, `011000` (positive) or `011001` (negative) |

```xml
<!-- Receipt-Request (abridged) -->
<ebicsRequest Version="H004" ...>
  <header authenticate="true">
    <static><HostID>EBICOHOST</HostID><TransactionID>…</TransactionID></static>
    <mutable><TransactionPhase>Receipt</TransactionPhase></mutable>
  </header>
  <body><TransferReceipt authenticate="true"><ReceiptCode>0</ReceiptCode></TransferReceipt></body>
</ebicsRequest>
```

## Return codes & error cases

| Situation | Phase | Return code | Placement |
| --- | --- | --- | --- |
| Success (Init/Transfer) | Init/Transfer | `000000` EBICS_OK | Header + Body |
| positive acknowledgement | Receipt | `011000` EBICS_DOWNLOAD_POSTPROCESS_DONE | Header |
| negative acknowledgement | Receipt | `011001` EBICS_DOWNLOAD_POSTPROCESS_SKIPPED | Header |
| Subscriber unknown/not `Ready`/no enc key | Init | `091002` EBICS_INVALID_USER_OR_USER_STATE | Body |
| No download data available | Init | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | Body |
| `NumSegments` > `MaxDownloadSegments` | Init | `091114` EBICS_MAX_SEGMENTS_EXCEEDED | Body |
| Unknown/removed `TransactionID` | Transfer/Receipt | `091101` EBICS_TX_UNKNOWN_TXID | Body |
| `SegmentNumber` missing / 0 or receipt without `ReceiptCode` | Transfer/Receipt | `091112` EBICS_INVALID_REQUEST_CONTENT | Body |
| `SegmentNumber` > `NumSegments` | Transfer | `091104` EBICS_TX_SEGMENT_NUMBER_EXCEEDED | Body |

The header/body placement follows automatically from `EbicsReturnCode.Kind` (`EbicsResponseFactory.Split`):
`011000`/`011001` are **technical** → header, the others **functional** → body. **No**
new return codes were needed — all already exist in the [catalog](../protocol/return-codes.md). All cases
are answered with **HTTP 200** and the return code in the `ebicsResponse` (see
[base rule](host.md)).

### ⚠️ Spec caveats

- **Phase/field distribution.** That `NumSegments` + segment 1 travel in the init response, the
  segments 2…N in the transfer, and `DataEncryptionInfo` **only** in the init response, is the canonical
  reading and **to be verified against the official EBICS annex**. Likewise the `SegmentNumber=1` echo
  in the init response.
- **ReceiptCode semantics.** `0 = positive → 011000`, `≠0 = negative → 011001` is the assumed
  assignment; to be verified against the annex.
- **Unsigned response.** The response is **not** signed (X002 = M4); no
  order signature is produced. Confidentiality is nonetheless present (order data encrypted for the
  subscriber's enc key).
- **Segment size raw vs. base64.** `SegmentSizeBytes` measures raw bytes; the reference of the EBICS segment
  boundary (raw vs. base64) is open (see [segmentation](segmentation.md)).
- **Orphaned transactions.** The idle timeout and the eviction (lazy on access + background
  sweeper) are implemented in **[#35](transaction-recovery.md)**: if the client aborts after init/transfer
  without a receipt, the transaction expires and is removed — the already dequeued data is then
  **re-enqueued** (as with a negative acknowledgement), so it is not lost.

## EBICS version mapping

The byte pipeline is version-agnostic; only the envelope/header details differ:

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Download order type | `OrderDetails/OrderType` = **FDL** | `OrderDetails/AdminOrderType` = **BTD** |
| Order parameters | `FDLOrderParams` | `BTDOrderParams` (BTF) |
| Transaction header | `TransactionID`/`NumSegments`/`SegmentNumber`+`lastSegment` — structurally identical | ditto |
| Response `DataTransfer` | `DataEncryptionInfo` + `OrderData` — structurally identical | ditto |

Exactly **one** `OrderData` element per transfer message (binding). The `TransactionID` is 16 bytes
(`hexBinary`); internally the store key is a hex string.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; request XML from committed Core bindings,
no proprietary fixtures):

- `DownloadTransactionTests` (`[Theory]` over H003/H004/H005) — **happy path 1 segment** (Init →
  `TransactionID` + `NumSegments=1` + `DataTransfer`; the delivered segment is decrypted with the **private**
  subscriber enc key and decompressed == original; Receipt(0) → `011000`, store empty)
  and **N segments** (small `SegmentSizeBytes` forces multiple segments; all reassembled +
  decrypted == original; only the init carries `DataEncryptionInfo`). Negative cases: no data
  (`090005`), subscriber not `Ready` (`091002`), unknown `TransactionID` (`091101`),
  `SegmentNumber` > N (`091104`) or 0 (`091112`), receipt with unknown TxID (`091101`).
  **Consumption:** after Receipt(0) → repeated download `090005`; after Receipt(1) → data available
  again. **Routing regression:** parallel upload + download TxID each land at the correct
  engine (`OwnsTransaction` disambiguation).
- `DownloadTransactionStoreTests` — `InMemoryDownloadTransactionStore` (Create/TryGet/Remove/Count,
  hex keying, duplicate create, `GetSegment` bounds) and `InMemoryDownloadDataProvider`
  (enqueue/dequeue FIFO, count, empty queue, isolation per (subscriber, order type)).
- `EbicsEndpointIntegrationTests` — download via the HTTP endpoint (`WebApplicationFactory`):
  enqueue order data via **admin API** `POST` → `POST /ebics` Init → decryption == original →
  Receipt(0) → `011000`; the admin queue is empty afterwards (consumption).

## Related documentation

- [Upload transaction (Initialisation + Transfer)](upload-transaction.md) — the mirrored counterpart (receive direction)
- [Segmentation, compression & base64 pipeline](segmentation.md) — the byte primitives used (send direction: `Split`)
- [Encryption E002](../protocol/encryption-e002.md) — hybrid encryption for the subscriber's enc key
- [HPB — retrieval of the bank keys](hpb.md) — model for the encrypted server→client payload flow (single-segment)
- [Master data management & admin API](master-data.md) — pattern of the unauthenticated admin API
- [Hostable server skeleton](host.md) — pipeline, error mapping, `EbicoServerOptions`
- [EBICS return-code catalog](../protocol/return-codes.md) — the triggered transaction/acknowledgement codes
- [ADR-0014 (download transaction engine)](../adr/0014-download-transaktions-engine.md) — dedicated engine, routing disambiguation, provider & consumption semantics
