# 0017 — Payment order processing (validation & status-report storage)

- Status: accepted
- Date: 2026-07-14

## Context

After the [BTF framework (#38)](0016-btf-framework-and-authorisation.md) and the
[upload transaction engine (#32)](0013-upload-transaction-engine.md), an upload ended
with the reassembled, decrypted, decompressed plaintext order data being held on the
`UploadTransaction` — without **business** processing. Issue #39 delivers the first
concrete order processing: SEPA payments (CCT/CIP → `pain.001`, CDD/CDB → `pain.008`)
are to be **validated** and a **status report (pain.002) stored for later delivery**.
Three decisions were to be made here: (a) how deeply the pain payload is validated, (b)
what "storage for later delivery (status reports)" concretely produces, and (c) which
submission conventions the emulator accepts.

## Decision

1. **Structural/semantic validation instead of XSD.**
   `EBICO.Core.Payments.SepaPaymentValidator` checks well-formedness, the `Document`
   root in the expected ISO namespace family
   (`urn:iso:std:iso:20022:tech:xsd:pain.001`/`pain.008`), the initiation root, the
   mandatory fields `GrpHdr/MsgId`/`CreDtTm`/`NbOfTxs`, ≥1 `PmtInf` + transaction and
   the two cross-checks (`NbOfTxs` = number of transactions, `CtrlSum` = sum of the
   `InstdAmt`). **No** ISO 20022 XSDs in the repo — consistent with the handling of
   proprietary/external schemas
   ([ADR-0003](0003-handling-proprietary-schemas.md), [ADR-0006](0006-commit-generated-xsd-bindings.md)).
   Elements are matched via **local names**, so every `pain.00x.001.NN` revision is
   accepted. (Rejected: full XSD validation — schema-acquisition infrastructure + CI
   dependency with no benefit for the emulator; the pluggable processor abstraction
   lets real XSD be retrofitted later.)

2. **Generate pain.002 and store it via `IDownloadDataProvider`.** On successful
   validation, `PainStatusReportBuilder` builds a positive **pain.002** (group status
   `ACCP`, echo of `OrgnlMsgId`/`OrgnlMsgNmId`) and the `SepaPaymentUploadProcessor`
   stores it via `IDownloadDataProvider.EnqueueAsync` under
   `EbicoServerOptions.PaymentStatusReportOrderType` (default `"PSR"`) for the
   submitting subscriber. This closes the upload→status-report loop and makes it
   observable via the provider/admin API. (Rejected: store only the raw payload — the
   status report is the actual business value.)

3. **Accept all three submission conventions.** H005 `BTU` + BTF, H003/H004 classic
   order code **directly** (`OrderType="CCT"`), and H003/H004 generic `FUL` +
   `FULOrderParams/FileFormat`. The effective order code is resolved centrally via
   `BtfOrderTypeCatalog.ResolveUploadOrderType(orderType, btf, fileFormat)` and used
   **before** the authorisation check (fix: FUL is authorised against `CCT`, not against
   `FUL`).

4. **Pluggable `IUploadOrderProcessor` instead of inline logic.** After decoding, the
   engine calls a processor registered via DI (default `SepaPaymentUploadProcessor`,
   `TryAddSingleton`). Order types the processor does not know keep the previous
   behaviour (only hold the plaintext). The resolved order code is stored alongside on
   the `UploadTransaction` at init, because the transfer phase no longer carries an
   order type.

## Consequences

- An invalid payload → `EBICS_INVALID_ORDER_DATA_FORMAT` (`090004`), **no** storage,
  `OrderRejected` event. A valid one → `000000`, pain.002 stored, `OrderAccepted` event.
- `PaymentStatusReportOrderType` (`"PSR"`) is a **best-effort placeholder** until the
  official External Code List; the **end-to-end downloading** of the status report
  (mapping FDL `FileFormat`/BTD BTF → PSR queue) follows with the
  [download orders (#40)](../server/download-transaction.md), since the download engine
  today dequeues by the raw order type (FDL/BTD).
- The ES/`SignatureFlag` check remains open (consistent with #32).
- `CIP` was added to the `BtfOrderTypeCatalog` (SCT/`INST`/pain.001), unambiguously
  distinguishable from CCT via the service option.

## Alternatives

- **Full ISO 20022 XSD validation** — rejected (see above), but retrofittable via the
  pluggable validation.
- **Store only the raw payload** instead of generating pain.002 — rejected (see above).
- **Order-specific single-phase handlers** (like INI/HIA) instead of a processor on the
  engine — rejected: payments are a multi-phase, segmented upload; the docking point is
  the completion of the transaction, not the single-shot resolver.
