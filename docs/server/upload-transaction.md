# Server: Upload transaction (initialisation + transfer)

> Implementation of **Issue #32** (Milestone M4 — Server: Transaction Engine). This page
> describes the server-side **receive engine** for an EBICS upload: the two-phase transaction
> consisting of **initialisation** (transaction-ID assignment, state setup) and **transfer**
> (segment-wise receipt, reassembly, decryption, decompression).
>
> Deliberately **included**: the transaction state machine (`UploadTransactionEngine`), the
> in-memory transaction store (`IUploadTransactionStore`), the 16-byte transaction ID,
> the phase routing in the pipeline, the buffering/reassembly of the segments
> (`EbicsSegmentation.Reassemble`), decryption (`EncryptionE002`) and decompression
> (`EbicsCompression`) of the reassembled order data, as well as the triggering of the
> transaction/segment return codes. Wired to the generic upload order types
> **FUL** (H003/H004) and **BTU** (H005).
> Deliberately **not yet**: the **signature verification of the order data** (ES / A00x) — the
> `SignatureData` from the initialisation is **retained**, but **not** cryptographically
> verified (see spec caveats, follow-up issue); the **download** transaction incl.
> receipt phase ([#33](download-transaction.md)); **recovery/timeouts** and the eviction of orphaned transactions (#35);
> the X002 request signature verification (the verify stage remains a no-op).

## Purpose

An EBICS upload transfers order data in **two phases**. In the **initialisation** the client
announces the transaction (order type, number of segments, encrypted transaction key,
electronic signature); the server assigns a **transaction ID** and sets up the
transaction state. In the **transfer** phase the client delivers the order data as
`base64(encrypt(compress(orderDataXml)))`, split into **segments** — one
`DataTransfer/OrderData` per message. On the last segment the server reassembles the segments,
decrypts them with the transaction key and decompresses them into the plaintext order data.

For this, #32 composes the already existing, policy-free primitives from #34/M2/M3
([segmentation](segmentation.md), [E002](../protocol/encryption-e002.md),
[compression](segmentation.md)) into a **state machine** and provides the **when/who**
(phases, transaction ID, envelope, segment policy) that the primitives deliberately left open.

## Flow

The server distinguishes the phase by the `TransactionPhase` field of the `ebicsRequest` (and — robust
against a missing field — by the presence of a `TransactionID` in the static header). An
`ebicsRequest` with `phase=Initialisation` and order type **FUL/BTU** starts the transaction; every
`ebicsRequest` with `TransactionID` continues it. All other `ebicsRequest`
(HCA/HCS/SPR …) run unchanged over the single-shot handler resolver.

### Phase 1 — Initialisation

| Step | Action |
| --- | --- |
| 1. Identity | check `HostID`/`PartnerID`/`UserID`; the subscriber must exist and be `Ready` (otherwise `091002`) |
| 2. Segment count | `Static/NumSegments` must be ≥ 1 and ≤ `EbicoServerOptions.MaxUploadSegments` (otherwise `091114`) |
| 3. Transaction key | decrypt `DataTransfer/DataEncryptionInfo/TransactionKey` with the **private** bank enc key (`EncryptionE002.DecryptTransactionKey`) |
| 4. Retain ES | store `DataTransfer/SignatureData` raw in the state (verification deferred) |
| 5. Create transaction | generate a 16-byte `TransactionID`, store the state (subscriber, OrderType, NumSegments, txKey, ES) in the `IUploadTransactionStore` |
| 6. Response | `ebicsResponse`, `phase=Initialisation`, `TransactionID`, `EBICS_OK` |

```xml
<!-- Request (gekürzt) -->
<ebicsRequest Version="H004" ...>
  <header authenticate="true">
    <static>
      <HostID>EBICOHOST</HostID> <PartnerID>PARTNER01</PartnerID> <UserID>USER01</UserID>
      <OrderDetails><OrderType>FUL</OrderType> ... </OrderDetails>
      <NumSegments>3</NumSegments>
    </static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase></mutable>
  </header>
  <body><DataTransfer>
    <DataEncryptionInfo authenticate="true"><TransactionKey>…</TransactionKey> …</DataEncryptionInfo>
    <SignatureData authenticate="true">…</SignatureData>   <!-- ES: einbehalten, nicht geprüft -->
  </DataTransfer></body>
</ebicsRequest>

<!-- Response (gekürzt) -->
<ebicsResponse Version="H004" ...>
  <header><static><TransactionID>…</TransactionID></static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase><ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body><ReturnCode>000000</ReturnCode></body>
</ebicsResponse>
```

### Phase 2 — Transfer (per segment 1…N)

| Step | Action |
| --- | --- |
| 1. Find transaction | `Static/TransactionID` → hex lookup in the store (missing → `091101`) |
| 2. Check segment number | `Mutable/SegmentNumber` in `[1, NumSegments]` (0 → `091112`, > N → `091104`) |
| 3. Buffer segment | store order-data bytes under `SegmentNumber`; duplicate → `091103` |
| 4. Completeness | on `lastSegment=true`: are all `NumSegments` present? (otherwise `011101`) |
| 5. Decode | `Reassemble` → `EncryptionE002.DecryptOrderData(txKey)` → `EbicsCompression.Decompress` (error → `090004`) |
| 6. Response | `ebicsResponse`, `phase=Transfer`, `TransactionID`, `SegmentNumber`, `EBICS_OK` |

`Reassemble` concatenates the segments in **segment-number order** (`SortedDictionary`); the
order of arrival is irrelevant. The reassembled, decrypted and decompressed
plaintext order data is held on the completed transaction (`UploadTransaction.OrderData`)
— the order-type-specific further processing is follow-up work.

## Return codes & error cases

| Situation | Return code | Placement |
| --- | --- | --- |
| Success (init/transfer) | `000000` EBICS_OK | Header + Body |
| Subscriber unknown / not `Ready` | `091002` EBICS_INVALID_USER_OR_USER_STATE | Body |
| `NumSegments` missing / 0 or segment number 0 | `091112` EBICS_INVALID_REQUEST_CONTENT | Body |
| `NumSegments` > `MaxUploadSegments` | `091114` EBICS_MAX_SEGMENTS_EXCEEDED | Body |
| unknown / expired `TransactionID` | `091101` EBICS_TX_UNKNOWN_TXID | Body |
| `SegmentNumber` > `NumSegments` | `091104` EBICS_TX_SEGMENT_NUMBER_EXCEEDED | Body |
| duplicate segment (replay) | `091103` EBICS_TX_MESSAGE_REPLAY | Body |
| `lastSegment` before completeness | `011101` EBICS_TX_SEGMENT_NUMBER_UNDERRUN | Header |
| Order data not decryptable/decompressable | `090004` EBICS_INVALID_ORDER_DATA_FORMAT | Body |

The transaction/segment codes are **control flow** and are set directly by the engine; the
decode errors (decryption/decompression) run over `OrderDataFault` → the existing
`EbicsErrorMapper` (`090004`). All cases are answered with **HTTP 200** and the return code in the
`ebicsResponse` (see [ground rule in the host skeleton](host.md)).

### ⚠️ Spec caveats

- **ES verification deferred.** The electronic signature (`SignatureData`, A005/A006)
  is read in and retained in the transaction state, but **not** verified — consistent with the
  single-phase key handlers (HCA/HCS). The order data is thus decrypted, but not
  authenticated. To be added later via `BankSignature.Verify` in a follow-up issue.
- **Init/transfer split.** That the `SignatureData`/`DataEncryptionInfo` travel in the initialisation
  and the `OrderData` segments exclusively in the transfer (no segment in the init) is the
  canonical reading and **must be verified against the official EBICS annex**.
- **BTF `SignatureFlag` (H005).** Whether an ES is required at all for a concrete BTU order
  is controlled spec-side by `BTUOrderParams/SignatureFlag`; #32 does not yet evaluate this.
- **S001 `OrderSignature` vs. `OrderSignatureData`.** For the later ES check it must be determined
  which of the two S001 carriers the sender populates (S002 knows only `OrderSignatureData`).
- **Segment size raw vs. base64.** `SegmentSizeBytes` measures raw bytes; the reference of the EBICS segment
  boundary (raw vs. base64) is open (see [segmentation](segmentation.md)).
- **Response fields.** `NumSegments` is not set in the upload response (per schema
  download-only); whether the transfer response must echo `SegmentNumber` is to be verified (it is
  set when present). The response is still **unsigned** (X002 = M4).
- **Orphaned transactions.** The idle timeout and eviction (lazy on access + background
  sweeper) are implemented in **[#35](transaction-recovery.md)**: uploads aborted after the initialisation
  expire and are removed; a transfer against an expired ID yields `091101`.

## EBICS version mapping

The byte pipeline is version-agnostic; only the envelope/header details differ:

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Upload order type | `OrderDetails/OrderType` = **FUL** | `OrderDetails/AdminOrderType` = **BTU** |
| Order parameters | `FULOrderParams` | `BTUOrderParams` (BTF) |
| ES schema | S001 (`OrderSignatureData`/`OrderSignature`) | S002 (`OrderSignatureData`) |
| Transaction header | `NumSegments`/`TransactionID`/`SegmentNumber`+`lastSegment` — structurally identical | ditto |

Exactly **one** `OrderData` element per transfer message (binding) — multiple segments per message
are structurally excluded. The `TransactionID` is 16 bytes (`hexBinary`); internally it is encoded as
a hex string (store key).

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; request XML from committed core bindings,
no proprietary fixtures):

- `UploadTransactionTests` (`[Theory]` over H003/H004/H005) — **happy path 1 segment** (init →
  `TransactionID` + `phase=Initialisation`; transfer → `phase=Transfer`, order data in the store ==
  original) and **N segments** (reassembly over several messages). The transfer response
  is deserialised and `TransactionPhase == Transfer` is checked (closes the
  `host.md` serialisation caveat). Negative cases: unknown `TransactionID` (`091101`),
  segment number > `NumSegments` (`091104`), duplicate (`091103`), `lastSegment` before completeness
  (`011101`), subscriber not `Ready` (`091002`), undecryptable order data (`090004`).
- `UploadTransactionStoreTests` — `InMemoryUploadTransactionStore` (Create/TryGet/Remove/Count,
  hex keying, duplicate create, null guards) and the segment-buffer logic of `UploadTransaction`
  (Buffered/Ready/Duplicate/Underrun, reassembly in segment order).
- `EbicsEndpointIntegrationTests` — upload over the HTTP endpoint (`WebApplicationFactory`,
  `POST /ebics`): init → `TransactionID` from the response → transfer → `EBICS_OK`, order data in the store.

## Related documentation

- [Download transaction (initialisation + transfer + receipt)](download-transaction.md) — the mirrored counterpart (send direction)
- [Segmentation, compression & base64 pipeline](segmentation.md) — the byte primitives used
- [Encryption E002](../protocol/encryption-e002.md) — transaction-key & order-data decryption
- [Hostable server skeleton](host.md) — pipeline, error mapping, `EbicoServerOptions`
- [Key change & suspension (HCA/HCS/SPR/HSA)](hca-hcs-spr-hsa.md) — model for the single-phase, encrypted upload
- [EBICS return code catalog](../protocol/return-codes.md) — the triggered transaction/segment codes
- [ADR-0013 (upload transaction engine)](../adr/0013-upload-transaktions-engine.md) — dedicated engine instead of resolver, in-memory transaction store
