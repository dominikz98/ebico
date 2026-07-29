# Server: Distributed electronic signature (HVU/HVZ/HVD/HVT/HVE/HVS)

> Implementation of **Issue #42** (Milestone M5 — Server: Orders & BTF). This page describes the
> **distributed electronic signature** (EDS / VEU): orders that still need further signatures after the
> upload are held server-side; further subscribers see them (HVU/HVZ),
> retrieve details (HVD/HVT) and sign (HVE) or cancel (HVS) them.
>
> Deliberately **included**: the six order types — **HVU** (overview of open orders), **HVZ** (overview
> with additional details), **HVD** (order status/detail), **HVT** (transaction details), **HVE** (signing),
> **HVS** (cancellation) across all three versions; a new, long-lived, partner-scoped **VEU store**
> (`IOpenVeuStore`/`InMemoryOpenVeuStore`); the version-aware core builder (`VeuResponseBuilder`) and the
> DTOs (`VeuOrderView`/`VeuSignerView`); the complete **multi-signature workflow in the server state**
> (parking → signing → release/cancellation) incl. `pain.002` storage on release; new `EbicsEventType` values
> (`VeuPending`/`VeuSigned`/`VeuReleased`/`VeuCancelled`) and the return code `091121`
> `EBICS_INVALID_ORDER_IDENTIFIER`.
> Deliberately **not yet**: the **cryptographic verification** of the electronic signatures (the ES is
> carried along, but not verified — a pervasive spec caveat since #32); the **complete
> ISO-20022 single-transaction decomposition** in HVT (here order-summary level); the bank-side
> **account signature rules** (here approximated via a request flag + fixed count); the **X002 signature**
> of the response (M4).

## Purpose

With the distributed electronic signature (EBICS *EDS — Distributed Electronic Signatures*) a
subscriber submits an order (typically a payment order) without all required
bank-technical signatures already being present. The order is held server-side in the
"signature folder" **temporarily**, until enough subscribers have signed it; only then is it
released. Other subscribers need four download orders for this (**HVU/HVZ** overview, **HVD/HVT**
detail) and two upload orders (**HVE** sign, **HVS** cancel).

Like the status/protocol orders ([#41](status-protocol-orders.md)), all six remain classic
**AdminOrderTypes** in H005 (no BTF service, see [BTF framework](btf-framework.md)). They dock onto the
existing transaction engines; structurally new is only the long-lived store for open orders.

## The VEU store (`IOpenVeuStore`)

Unlike the transient transaction stores (idle timeout, [#35](transaction-recovery.md)), an
open VEU order lives **partner-scoped** until fully signed (released) or cancelled. The store is
keyed by `(HostId, PartnerId, OrderId)`; the **OrderId** (4 characters, pattern `[A-Z][A-Z0-9]{3}`)
is assigned by the store on adding (leading `V` + running base-36 number). An `OpenVeuOrder` holds:
order data (+ size, SHA-256 digest), order type, submitter (`OriginatorInfo`), number of required/provided
signatures and the list of those who have already signed (`SignerInfo`). Default: `InMemoryOpenVeuStore`,
pluggable via `TryAddSingleton`.

## Submission conventions & routing

The order codes are submitted **directly** (`AdminOrderType` in H005, `OrderType` in H003/H004);
`BtfOrderTypeCatalog.ResolveUpload/DownloadOrderType` passes the raw code through. HVU/HVZ/HVD/HVT run as
**downloads**, HVE/HVS as **uploads**:

| Order | Direction | Engine detection | OrderParams |
| --- | --- | --- | --- |
| HVU / HVZ | Download | `DownloadTransactionEngine.IsDownloadOrderType` (`VeuOrderTypes.IsVeuDownloadOrderType`) | none |
| HVD / HVT | Download | ditto | `HV[DT]OrderParams/OrderID` (target order) |
| HVE / HVS | Upload | `UploadTransactionEngine.IsUploadOrderType` (`VeuOrderTypes.IsVeuUploadOrderType`) | `HV[ES]OrderParams/OrderID` (target order) |

The engines were extended with the OrderID extraction from the `Hv*OrderParams` (`DownloadOrderRequest.OrderId`
and `UploadOrderContext.OrderId` respectively). Generation/processing is spread across pluggable processors: a
new `VeuOverviewDownloadProcessor` (HVU/HVZ/HVD/HVT, projected from the store) and a new
`VeuSignatureUploadProcessor` (HVE/HVS). For this the upload engine now takes — symmetrically to the download engine
— `IEnumerable<IUploadOrderProcessor>` and picks the first matching `CanProcess`.

## Flow

### 1. Parking (upload of an order for distributed signing)

A payment order (CCT/CDD/CDB/CIP) is submitted for distributed signing when the request signal
is set (see [Trigger](#park-trigger)). The `SepaPaymentUploadProcessor` validates the pain payload as
usual and then stores the order — instead of immediately placing the `pain.002` — in the `IOpenVeuStore`:

| Step | Action |
| --- | --- |
| 1. Validate | `SepaPaymentValidator` (unchanged); invalid → `090004`, `OrderRejected` |
| 2. First signature | does the submitter carry a bank-technical class (E/A/B) for the order type? → first `SignerInfo` |
| 3. Park | create `OpenVeuOrder` (`NumSigRequired` = `EbicoServerOptions.VeuRequiredSignatures`, default 2), event `VeuPending` |

If the first signature already satisfies the required number (e.g. class E with `VeuRequiredSignatures=1`),
the order is **not** parked, but released immediately like an ordinary upload.

### 2. See & check (HVU/HVZ/HVD/HVT)

The `VeuOverviewDownloadProcessor` projects the open orders of the partner via the `VeuResponseBuilder`
into the version-specific bindings:

- **HVU/HVZ** list **all** open orders of the partner (empty list → empty, valid document, no
  error). HVZ additionally carries digest/size details.
- **HVD/HVT** address **one** order via the `OrderID` from the OrderParams. If the ID is missing or
  identifies no open order → the processor declines (`null`) → engine reports `090005`.

### 3. Signing (HVE) & release

The `VeuSignatureUploadProcessor` processes an HVE against the referenced `OrderID`:

| Step | Action |
| --- | --- |
| 1. Resolve | order by `(Host, Partner, OrderID)`; unknown → `091121` |
| 2. Authorise | the signer must satisfy `Subscriber.CanAuthorize(underlying order type)` (E/A/B) → otherwise `090003` |
| 3. Sign | double signature by the same user → `090004`; otherwise record the signature, event `VeuSigned` |
| 4. Release | if `NumSigDone` reaches the required number → place `pain.002` for the submitter, remove the order, events `VeuReleased` + `OrderAccepted` |

### 4. Cancelling (HVS)

An HVS removes the order (event `VeuCancelled`). Allowed for the **submitter** or a subscriber
authorised to sign the underlying order type (otherwise `090003`); an unknown
order → `091121`. A cancelled order is **never** released (no `pain.002`).

<a id="park-trigger"></a>

### Park trigger

Whether an upload goes into distributed signing is decided by an **explicit, non-breaking request signal**
(default: immediate release like [#39](payment-orders.md)):

| Version | Signal | Default (not distributed) |
| --- | --- | --- |
| H005 | presence of `BTUOrderParams/SignatureFlag` | no `SignatureFlag` |
| H003/H004 | `OrderAttribute = OZHNN` | `OrderAttribute = DZHNN` |

A class-based trigger was ruled out because the #39 uploads seed with transport class (T) — it would
wrongly park them.

### Example — HVU (H005, abridged)

```xml
<HVUResponseOrderData xmlns="urn:org:ebics:H005">
  <OrderDetails>
    <Service><ServiceName>SCT</ServiceName><MsgName>pain.001</MsgName></Service>
    <OrderID>V001</OrderID>
    <OrderDataSize>1234</OrderDataSize>
    <SigningInfo readyToBeSigned="true" NumSigRequired="2" NumSigDone="1" />
    <SignerInfo>
      <PartnerID>PARTNER01</PartnerID><UserID>USER01</UserID><Name>Alice</Name>
      <Timestamp>2026-07-15T10:00:00Z</Timestamp>
      <Permission AuthorisationLevel="A" />
    </SignerInfo>
    <OriginatorInfo>
      <PartnerID>PARTNER01</PartnerID><UserID>USER01</UserID><Name>Alice</Name>
      <Timestamp>2026-07-15T10:00:00Z</Timestamp>
    </OriginatorInfo>
  </OrderDetails>
</HVUResponseOrderData>
```

In H003/H004 `OrderDetails` carries the classic `OrderType` (e.g. `CCT`) instead of `Service`.

## Return codes & error cases

| Situation | Return code | Order |
| --- | --- | --- |
| Success (parking / signing / release / cancellation / download) | `000000` EBICS_OK | all |
| unknown OrderID | `091121` EBICS_INVALID_ORDER_IDENTIFIER | HVE/HVS |
| signer not authorised to sign / HVS unauthorised | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | HVE/HVS |
| double signature / order already complete | `090004` EBICS_INVALID_ORDER_DATA_FORMAT | HVE |
| no open order for the OrderID / empty detail request | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | HVD/HVT |
| Order type not authorised (engine gate) | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | all |

The remaining transaction/segment codes come unchanged from the
[upload](upload-transaction.md)/[download transaction](download-transaction.md).

### ⚠️ Spec caveats

- **ES not verified.** The electronic signature carried by HVE is not cryptographically
  checked; "signing" means that a subscriber **authorised** for the underlying order type
  (E/A/B permission) has submitted an HVE. The `DataDigest` is a simple SHA-256 over the order data,
  not the canonical EBICS ES hash.
- **Park trigger & signature count.** Whether an order is to be signed distributedly comes from the
  request signal (`SignatureFlag`/`OrderAttribute`), not from bank-side account signature rules; the
  required number is a fixed server option (`VeuRequiredSignatures`, default 2).
- **HVT order-summary level.** HVT delivers a single `OrderInfo` (message name of the order), not a
  complete ISO-20022 decomposition per single transaction.
- **Duplicate/complete code.** For "already signed"/"already complete" there is no dedicated
  EBICS code; here `090004` (best-effort).
- **Unsigned response.** X002 still deferred (M4), as with the transactions.

## EBICS version mapping

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Order identity (response) | `OrderType` (+ H004 `FileFormat`) | `Service` (BTF `RestrictedServiceType`) |
| Park trigger (upload) | `OrderAttribute = OZHNN` | `BTUOrderParams/SignatureFlag` |
| HVT `OrderInfo` | `OrderFormat` (string) | `MsgName` (`MessageType`) |
| HVZ `AdditionalOrderInfo` | not present | present |
| Namespace | `http://www.ebics.org/H003` (H003) · `urn:org:ebics:H004` | `urn:org:ebics:H005` |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; no proprietary fixtures):

- `Core/Administrative/VeuOrderTypesTests` — classification download/upload/negative.
- `Core/Administrative/VeuResponseBuilderTests` — HVU/HVZ/HVD/HVT over H003/H004/H005 (namespace, OrderID,
  signing/signer info, `Service` vs. `OrderType`), empty overview.
- `Server/OpenVeuStoreTests` — OrderID assignment/pattern, listing per partner, sign state machine
  (signature/duplicate/completion), remove.
- `Server/VeuOrdersTests` — **end-to-end** through the pipeline across all versions: parking → HVU → HVD → HVT →
  HVE → release (`pain.002` placed, overview empty); empty HVU; HVS cancellation; negative: unknown OrderID
  (`091121`), not authorised to sign (`090003`), double signature (`090004`).

## Related documentation

- [Upload transaction](upload-transaction.md) / [Download transaction](download-transaction.md) — the engines that #42 hooks into
- [Upload orders: payments](payment-orders.md) — the parked orders and the `pain.002` storage on release (#39)
- [Status & protocol orders](status-protocol-orders.md) — the sister pattern (#41): AdminOrderTypes, pluggable download processors
- [Event/protocol store (`IEventLog`)](event-log.md) — the VEU events (`VeuPending`/`VeuSigned`/`VeuReleased`/`VeuCancelled`)
- [BTF framework (H005)](btf-framework.md) — admin vs. BTF order types, authorisation check
- [ADR-0020 (distributed electronic signature)](../adr/0020-veu-orders.md) — VEU store, park trigger, signing authorisation, release
