# Server: Upload orders — payments (CCT/CDD/CDB/CIP)

> Implementation of **Issue #39** (Milestone M5 — Server: Orders & BTF). This page describes the
> first **business order processing** on top of the [upload transaction](upload-transaction.md): the
> **validation** of uploaded SEPA payment payloads and the **storing of a
> status report (pain.002) for later delivery**.
>
> Deliberately **included**: the pluggable `IUploadOrderProcessor` (default `SepaPaymentUploadProcessor`),
> the structural pain validation (`EBICO.Core.Payments.SepaPaymentValidator`), the pain.002 builder
> (`PainStatusReportBuilder`), the resolution of the effective order code across all three
> submission conventions (`BtfOrderTypeCatalog.ResolveUploadOrderType`) and the storing of the
> status report via the `IDownloadDataProvider`. Order types: **CCT** (SEPA Credit Transfer /
> `pain.001`), **CIP** (SEPA Instant / `pain.001`), **CDD** (SEPA Direct Debit CORE / `pain.008`),
> **CDB** (SEPA Direct Debit B2B / `pain.008`).
> Deliberately **not yet**: a real **ISO 20022 XSD validation** (structural/semantic instead of XSD);
> the **ES/`SignatureFlag` check** (still deferred, consistent with
> [#32](upload-transaction.md)); the **end-to-end download** of the status report (mapping
> FDL `FileFormat`/BTD BTF → `PSR` queue): the generate-on-demand download machine arrived with the
> [download orders (#40)](statement-orders.md), but the `PSR` mapping itself remains open (no
> BTF/order type points to `PSR`).

## Purpose

A payments order is a **generic, segmented upload** (no dedicated handler): it
runs over the [upload transaction](upload-transaction.md) (FUL/BTU) or — for the classic
order types — directly over the order code. After reassembly/decryption/decompression, the
plaintext payload previously lived only on the transaction. #39 hooks in the **business
processing** at this point: the payload is checked against the expected pain message and — on success — a
positive **pain.002 Customer Payment Status Report** is generated and stored for later delivery.

## Submission conventions & routing

The emulator accepts all three usual EBICS conventions; the **effective** order code is resolved centrally
via `BtfOrderTypeCatalog.ResolveUploadOrderType(orderType, btf, fileFormat)` (order:
BTF → FileFormat → raw code):

| Version | Convention | Example | Resolution |
| --- | --- | --- | --- |
| H005 | `AdminOrderType=BTU` + `BTUOrderParams/Service` (BTF) | Service `SCT`/`pain.001` | → `CCT` |
| H003/H004 | classic `OrderType` **directly** | `OrderType=CCT` | → `CCT` |
| H003/H004 | generic `OrderType=FUL` + `FULOrderParams/FileFormat` | `FileFormat=pain.001.001.09` | → `CCT` |

The routing detection (`UploadTransactionEngine.IsUploadOrderType`) now knows, besides `FUL`/`BTU`, also
the direct upload codes (`BtfOrderTypeCatalog.IsUploadOrderType`). The resolved code is used **before** the
authorisation check ([#38](btf-framework.md)) and also stored on the `UploadTransaction`
(`EffectiveOrderType`), because the transfer phase no longer carries an order type.

> **Note on FUL FileFormat resolution:** CDD (CORE) and CDB (B2B) both carry `pain.008`; from the
> FileFormat alone the service option cannot be derived — the un-optioned default (CDD) wins.
> For B2B via FUL an explicit marker would be needed (best-effort, see [ADR-0017](../adr/0017-payment-order-processing.md)).

## Flow

The order type detection/authorisation happens in the **initialisation**, the processing on the
**last segment** of the **transfer** phase (where the full payload is available):

| Step | Phase | Action |
| --- | --- | --- |
| 1. Resolve | Init | determine the effective order code (BTF/FileFormat/direct), check against the authorisation (otherwise `090003`), store on the transaction |
| 2. Decode | Transfer (last) | reassemble → E002 decrypt → decompress (error → `090004`) |
| 3. Process | Transfer (last) | if the order code is a payments type, the engine calls the `IUploadOrderProcessor` |
| 4a. Validate | — | `SepaPaymentValidator.Validate(orderType, payload)` — invalid → `090004`, **no** storing, `OrderRejected` event |
| 4b. Store | — | build pain.002 (`OrgnlMsgId`/`OrgnlMsgNmId`, `GrpSts=ACCP`) and store via `IDownloadDataProvider.EnqueueAsync(subscriber, "PSR", …)`, `OrderAccepted` event |
| 5. Respond | Transfer | `ebicsResponse`, `phase=Transfer`, `EBICS_OK` (or `090004` on reject) |

### Validation (structural/semantic)

`SepaPaymentValidator` (in `EBICO.Core.Payments`) checks — **without** XSD, elements by local names:

- well-formed XML; `Document` root in the expected ISO namespace family
  (`urn:iso:std:iso:20022:tech:xsd:pain.001` or `…pain.008`);
- initiation root (`CstmrCdtTrfInitn` / `CstmrDrctDbtInitn`);
- `GrpHdr/MsgId` and `GrpHdr/CreDtTm` (not empty), `GrpHdr/NbOfTxs`;
- ≥1 `PmtInf` and ≥1 transaction (`CdtTrfTxInf` / `DrctDbtTxInf`);
- **cross-check 1:** `NbOfTxs` == number of transactions;
- **cross-check 2:** if `CtrlSum` is present: == sum of the `InstdAmt`.

```xml
<!-- pain.001 (CCT), abridged -->
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.001.001.09">
  <CstmrCdtTrfInitn>
    <GrpHdr><MsgId>MSG-CCT-1</MsgId><CreDtTm>2026-07-14T10:00:00</CreDtTm>
      <NbOfTxs>2</NbOfTxs><CtrlSum>150.00</CtrlSum></GrpHdr>
    <PmtInf> … <CdtTrfTxInf><Amt><InstdAmt Ccy="EUR">100.00</InstdAmt></Amt> … </CdtTrfTxInf>
                <CdtTrfTxInf><Amt><InstdAmt Ccy="EUR">50.00</InstdAmt></Amt> … </CdtTrfTxInf> </PmtInf>
  </CstmrCdtTrfInitn>
</Document>
```

### Status report (pain.002)

`PainStatusReportBuilder` generates a minimal, group-level **pain.002** (default
`pain.002.001.03`):

```xml
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.002.001.03">
  <CstmrPmtStsRpt>
    <GrpHdr><MsgId>PSR-…</MsgId><CreDtTm>2026-07-14T10:00:00Z</CreDtTm></GrpHdr>
    <OrgnlGrpInfAndSts><OrgnlMsgId>MSG-CCT-1</OrgnlMsgId>
      <OrgnlMsgNmId>pain.001.001.09</OrgnlMsgNmId><GrpSts>ACCP</GrpSts></OrgnlGrpInfAndSts>
  </CstmrPmtStsRpt>
</Document>
```

It is stored under `EbicoServerOptions.PaymentStatusReportOrderType` (default `"PSR"`) for the submitting
subscriber and is observable via the [admin API](master-data.md) (`GET
…/subscribers/{userId}/downloads/PSR`) or the `IDownloadDataProvider`.

## Return codes & error cases

| Situation | Return code | Storing |
| --- | --- | --- |
| success (validated, status report stored) | `000000` EBICS_OK | header + body |
| pain payload invalid (structure/cross-check) | `090004` EBICS_INVALID_ORDER_DATA_FORMAT | body |
| no authorisation for the (resolved) order type | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | body |

The remaining transaction/segment codes (`091101`/`091104`/…) come unchanged from the
[upload transaction](upload-transaction.md).

### ⚠️ Spec caveats

- **No real XSD validation.** Structure/semantics instead of ISO 20022 XSD (ADR-0017); pluggable for
  real XSD via a replaced `IUploadOrderProcessor`/validator.
- **ES/`SignatureFlag` still unchecked.** The payload is decrypted, but not authenticated
  (consistent with [#32](upload-transaction.md)).
- **Status report download open.** `PaymentStatusReportOrderType` (`"PSR"`) is a best-effort
  placeholder. The [download orders (#40)](statement-orders.md) switched the download engine so that
  it dequeues by the **resolved** order type (instead of just by the raw FDL/BTD); but there is still
  **no** BTF/order type that points to the `PSR` queue, so the status report is only observable via the
  [admin API](master-data.md). The `PSR` mapping remains a follow-up step.
- **FUL B2B ambiguity.** CDD/CDB share `pain.008`; over FUL/FileFormat the CORE default (CDD) wins.
- **pain.002 version.** Fixed `pain.002.001.03` instead of being strictly coupled to the upload version.

## EBICS version mapping

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Order identity | `OrderType` directly **or** `FUL` + `FULOrderParams/FileFormat` | `AdminOrderType=BTU` + `BTUOrderParams/Service` (BTF) |
| Resolution → code | direct / FileFormat family → CCT/CDD/CDB/CIP | BTF → classic code (catalog) |
| pain payload | identical (`pain.001`/`pain.008`, checked version-agnostically) | ditto |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; pain XML from the committed
`Infrastructure/PainSamples` builder, no proprietary fixtures):

- `Core/Payments/SepaPaymentValidatorTests` — valid `pain.001`/`pain.008`; negative cases: wrong
  message family, missing `MsgId`, `NbOfTxs` mismatch, `CtrlSum` mismatch, no `PmtInf`,
  malformed XML, unknown order type.
- `Core/Payments/PainStatusReportBuilderTests` — pain.002 echoes `OrgnlMsgId`/`OrgnlMsgNmId`, `GrpSts=ACCP`.
- `Core/Btf/BtfOrderTypeCatalogTests` — CIP seed, `IsUploadOrderType`, `TryGetOrderTypeByFileFormat`,
  `ResolveUploadOrderType` (all three conventions).
- `Server/PaymentUploadTests` (`[Theory]` over H003/H004/H005) — CCT/CDD/CDB **end-to-end** through the
  pipeline (H005 BTU+BTF, H003/H004 direct **and** FUL+FileFormat): `000000`, status report stored in the
  provider, dequeued pain.002 with matching `OrgnlMsgId`; invalid payload → `090004`, nothing stored,
  transaction not completed.

## Related documentation

- [Upload transaction (initialisation + transfer)](upload-transaction.md) — the receive machine that #39 hooks into
- [BTF framework (H005)](btf-framework.md) — BTF↔order type catalog, authorisation check
- [Download transaction](download-transaction.md) — the storing/delivery channel (`IDownloadDataProvider`)
- [Download orders: account statements & reports](statement-orders.md) — the download counterpart (#40); switched the engine to dequeue by resolved order type (`PSR` mapping still open)
- [Event/log store (IEventLog)](event-log.md) — `OrderAccepted`/`OrderRejected` events
- [Master data management](master-data.md) — authorisations, admin API (download queue)
- [EBICS return code catalog](../protocol/return-codes.md) — `090004`/`090003`
- [ADR-0017 (payments order processing)](../adr/0017-payment-order-processing.md) — validation depth, status report storing, routing
