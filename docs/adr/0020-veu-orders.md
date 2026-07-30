# 0020 — Distributed electronic signature (VEU store, parking/signing workflow)

- Status: accepted
- Date: 2026-07-16

## Context

Issue #42 (Milestone M5) requires the **distributed electronic signature** (EBICS
*EDS*): HVU/HVZ (overview of open orders), HVD/HVT (detail) as well as HVE (sign) and
HVS (cancel). At the core is a **multi-signature workflow in the server state** — an
uploaded order that still needs further signatures must be held and signed by
additional subscribers.

Already present: the [upload](../server/upload-transaction.md) and
[download transaction](../server/download-transaction.md) (#32/#33), the processor
pattern (`IUploadOrderProcessor`/`IDownloadOrderProcessor`), payment processing with
`pain.002` storage (#39, [ADR-0017](0017-zahlungsverkehr-order-verarbeitung.md)), the
[`IEventLog`](../server/event-log.md) (#69) and — committed — the generated VEU
bindings (`HVU/HVZ/HVD/HVT/HVE/HVS…`) for all three versions. **Not** present: a
server-side state for "uploaded but not (fully) signed yet" — the previous processing
completes an upload immediately.

To be decided: (1) where open orders are held, (2) how the server recognises that an
upload is to be signed distributedly (without breaking the #39 uploads), (3) who may
sign, (4) what happens on full signing, (5) how the six orders are wired up.

## Decision

1. **New, long-lived VEU store** `IOpenVeuStore` (default `InMemoryOpenVeuStore`),
   **partner-scoped** by `(HostId, PartnerId, OrderId)` — deliberately separate from the
   transient transaction stores (no idle timeout): an open order lives until release or
   cancellation. The **OrderId** (4 characters, `[A-Z][A-Z0-9]{3}`) is assigned by the
   store (leading `V` + base-36 counter). An `OpenVeuOrder` holds order data (+ SHA-256
   digest), order type, submitter, required/provided signatures and the signer list; the
   sign/cancel transitions are encapsulated in the store
   (`TrySignAsync`/`TryCancelAsync`).

2. **Parking trigger as an explicit request signal** (default: immediate release like
   #39): for H005 the presence of `BTUOrderParams/SignatureFlag`, for H003/H004
   `OrderAttribute=OZHNN`. A class-based trigger was ruled out because the #39 uploads
   seed with the transport class (T) — it would wrongly park them. The
   `SepaPaymentUploadProcessor` validates the pain payload unchanged and then parks
   (instead of storing `pain.002`), provided the signal is set; the number of required
   signatures is the fixed option `EbicoServerOptions.VeuRequiredSignatures` (default 2),
   the first signature being the bank-technical class (E/A/B) of the submitter for the
   order type.

3. **Signing authorisation via the existing signature-class model**: an HVE is accepted
   only if the signer satisfies `Subscriber.CanAuthorize(underlying order type)` (holds
   E/A/B), otherwise `090003`. A double signature by the same user → `090004`, an unknown
   OrderId → new return code `091121` `EBICS_INVALID_ORDER_IDENTIFIER`. HVS may be done
   by the submitter or a signing-authorised subscriber.

4. **Release = `pain.002` storage for the submitter** (symmetric to the immediate accept
   from #39): once the signature count reaches `VeuRequiredSignatures`, the order is
   removed from the store and the positive `pain.002` is stored via
   `IDownloadDataProvider` (shared helper `PaymentStatusReportFiling`, also used by the
   immediate-accept path); events `VeuReleased` + `OrderAccepted`. Parking/signing/cancel
   write `VeuPending`/`VeuSigned`/`VeuCancelled` (new `EbicsEventType` values).

5. **Wiring to the existing engines**: HVU/HVZ/HVD/HVT as a download via a new
   `VeuOverviewDownloadProcessor` (projects the store via the version-aware
   `VeuResponseBuilder`), HVE/HVS as an upload via a new `VeuSignatureUploadProcessor`.
   `IsUpload/IsDownloadOrderType` additionally recognise the codes (`VeuOrderTypes`). The
   OrderID of the detail/signing orders is extracted from the `Hv*OrderParams` (new fields
   `DownloadOrderRequest.OrderId`/`UploadOrderContext.OrderId`). For this the **upload
   engine** now takes — symmetrically to the download engine —
   `IEnumerable<IUploadOrderProcessor>` and picks the first matching `CanProcess`
   (previously exactly one).

## Consequences

- The emulator reflects the full EDS cycle (parking → overview → detail → signing →
  release/cancellation) across all three versions, testable end-to-end through the
  pipeline — without proprietary fixtures.
- The switch of the upload engine to `IEnumerable<IUploadOrderProcessor>` is additive;
  the SEPA processor (#39) stays registered, third-party processors can be added via
  `AddSingleton`. The #39 upload builder now explicitly sets `OrderAttribute=DZHNN`
  (behaviour-neutral, prevents mis-parking due to the enum default).
- **Spec caveats:** the ES is not verified (digest = plain SHA-256); parking trigger and
  signature count approximate the bank-side account signing rules; HVT is order-summary
  (no ISO 20022 single-transaction decomposition); for "already signed"/"already
  complete" there is no dedicated EBICS code (best-effort `090004`); the response stays
  unsigned (X002 = M4).

## Alternatives

- **Hold orders in the transaction store** — wrong, because it is idle-timeout-volatile
  and transaction-scoped; an open VEU must live for days partner-wide. Rejected in favour
  of a dedicated store (pattern like `IDownloadDataProvider`).
- **Class-based parking trigger** (T/A/B park, E immediate) — breaks the #39 tests
  (transport class) and mixes authorisation with submission intent; rejected in favour of
  the explicit request signal.
- **HVE/HVS as dedicated order handlers** (instead of an upload processor) — would have
  duplicated the segment/decryption pipeline; rejected in favour of the existing upload
  transaction.
- **No `pain.002` on release** (only removal + event) — simpler, but asymmetric to the
  immediate accept and without feedback to the submitter; rejected in favour of symmetry
  with #39.
- **VEU for H005 only** — less effort, but the bindings are present for all three
  versions and the remaining M5 orders are version-complete; rejected in favour of parity.
