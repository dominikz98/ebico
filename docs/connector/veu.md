# Connector: Distributed electronic signature (HVU/HVZ/HVD/HVT/HVE/HVS)

> Implementation of **issue #124** ([ADR-0030](../adr/0030-defaults-und-clientseitige-veu-anbindung.md)).
> On the server side the VEU has existed since **#42** ([VEU orders](../server/veu-orders.md), ADR-0020) — this
> page describes the **client** side, which was missing up to #124.

The *distributed electronic signature* (VEU/EDS) is EBICS's multi-eyes workflow: an order is
submitted, **parked** by the bank instead of executed, and released only once enough authorised
subscribers have signed it.

## Why this page exists

The [order/BTF coverage matrix](../server/order-coverage-matrix.md) listed HVU–HVS as ✅ for all three
versions — that held for the **server**. With the shipped connector the workflow was not runnable in
any version; three gaps interlocked:

1. **H005 uploads required a BTF.** HVE/HVS are administrative order types *without* a BTF, so they
   were rejected client-side with `EbicsConfigurationException` and never reached the wire. The
   download path had long known this case — which is why HVU/HVZ worked.
2. **There was no field for the `OrderID`.** HVE/HVS/HVD/HVT refer to *one* parked order.
   On H003/H004 they did go out, but consequently acknowledged with `091121`.
3. **No order could be parked at all.** The `OrderAttribute` was hard-wired to `DZHNN` in all upload
   envelopes, and the connector knew no `SignatureFlag`.

Maxim: an order type is available from a user's perspective only once the shipped client can send it.
The coverage matrix therefore separates **server** and **client** availability since #124.

## The flow

```csharp
// 1) Auftrag einreichen und zum Parken markieren.
var submitted = await client.Send(new CctUploadRequest
{
    Pain001 = painBytes,
    DistributedSignature = true,   // H005: BTUOrderParams/SignatureFlag · H003/H004: OrderAttribute=OZHNN
});

// 2) Offene Aufträge abholen — hier steht die vom Server vergebene OrderID.
var overview = await client.Send(new HvuDownloadRequest());

// 3) Status eines einzelnen Auftrags (optional).
var detail = await client.Send(new HvdDownloadRequest
{
    Order = new VeuOrderReference { OrderId = "V001", OrderType = "CCT" },
});

// 4) Zeichnen — durch einen ANDEREN Teilnehmer (siehe unten).
var signed = await client.Send(new HveUploadRequest
{
    Order = new VeuOrderReference { OrderId = "V001", OrderType = "CCT" },
});

// ... oder stornieren.
var cancelled = await client.Send(new HvsUploadRequest
{
    Order = new VeuOrderReference { OrderId = "V001", OrderType = "CCT" },
});
```

Once the number of signatures reaches `EbicoServerOptions.VeuRequiredSignatures` (default **2**), the
server releases the order, files the `pain.002` status report for the submitter and removes it from
the VEU store.

> **The submitter counts too.** If the submitting subscriber holds a bank-technical authorisation
> (E/A/B) for the order type, the emulator already counts their submission as the **first** signature
> (`SepaPaymentUploadProcessor`). A second HVE from the same subscriber is rejected as a double
> signature — the releasing HVE must come from a **different** subscriber. That is precisely the
> purpose of the VEU.

## API

| Type | Order type | Purpose |
| --- | --- | --- |
| `UploadRequest.DistributedSignature` / `CctUploadRequest…` | — | park trigger on the submitting upload |
| `HvuDownloadRequest` | `HVU` | overview of the open orders |
| `HvzDownloadRequest` | `HVZ` | overview with payment details |
| `HvdDownloadRequest` | `HVD` | status/detail of an order |
| `HvtDownloadRequest` | `HVT` | transaction details of an order 🟡 |
| `HveUploadRequest` | `HVE` | add signature |
| `HvsUploadRequest` | `HVS` | cancel/reject order |

All six are registered via `AddEbicoUpload()` / `AddEbicoDownload()` — no separate
`AddEbicoVeu()`, because they share the executor and envelope builder with the other orders.

### `VeuOrderReference`

Names the parked order. Only `OrderId` is mandatory:

| Property | Meaning |
| --- | --- |
| `OrderId` | The order ID assigned by the bank from HVU/HVZ. **Mandatory.** |
| `PartnerId` | Customer of the submitter; the default is your own `PartnerID`. |
| `OrderType` | Classic order type of the referenced order (H003/H004); serves the BTF resolution on H005. |
| `Btf` | H005 `Service` of the referenced order; otherwise derived from `OrderType`. |
| `FileFormat` | Only H004, if the order was submitted as a generic `FUL`. |

If the reference is missing on HVE/HVS (upload) or HVD/HVT (download), the call fails **client-side**
with `EbicsConfigurationException` — with a message that names what is missing, instead of the bank's
generic `091121`.

## Version dispatch

| Aspect | H003 | H004 | H005 |
| --- | --- | --- | --- |
| Park trigger | `OrderAttribute=OZHNN` | `OrderAttribute=OZHNN` | `BTUOrderParams/SignatureFlag` |
| Order type in the header | `OrderType` | `OrderType` | `AdminOrderType` (**no** BTU/BTD) |
| Order params | `Hve`/`Hvs`/`Hvd`/`HvtOrderParamsType` with `PartnerID`/`OrderType`/`OrderID` | ditto, plus `FileFormat` | ditto, but `Service` (BTF) instead of `OrderType` |

## Return codes

| Code | Meaning |
| --- | --- |
| `000000` | HVE/HVS accepted |
| `011000` | HVU/HVZ/HVD/HVT delivered (download post-processing) |
| `090003` | subscriber may not sign the underlying order |
| `090004` | double signature or already fully signed |
| `090005` | HVD/HVT: no data for the given `OrderID` |
| `091121` | `EBICS_INVALID_ORDER_IDENTIFIER` — unknown `OrderID` |

## Spec caveats

- **Only the `OrderID` is evaluated on the server side.** The other fields of the `VeuOrderReference`
  (PartnerID, OrderType/Service, FileFormat) are emitted schema-compliantly, but the emulator keys its
  VEU store solely on the `OrderID`. Unverified against a real bank.
- **The park trigger is design intent.** That `OZHNN` or `SignatureFlag` are the decisive signals is
  not verified against the official annexes (schemas proprietary,
  [ADR-0003](../adr/0003-umgang-mit-proprietaeren-schemas.md)).
- **The HVE signature is not checked.** `HveUploadRequest.SignaturePayload` carries a minimal
  placeholder by default; the emulator logs *that* an authorised party signed (ADR-0020).
- **HVT is order-summary** — no ISO-20022 single-transaction decomposition.
- **Release by count**, not by account-related signature rules.

## Tests

`tests/EBICO.Tests/E2E/VeuE2ETests.cs` — real round-trip Connector ↔ Server for each H003/H004/H005:

- park an order and find it again in HVU (incl. `1/2` signatures of the submitter),
- release by a **second** subscriber (`EbicsE2EHarness.AddCoSignerAsync`),
- a double signature by the submitter is rejected,
- cancellation via HVS,
- HVD resolves the referenced `OrderID` — and finds nothing for a foreign ID (`090005`),
- unknown `OrderID` on HVE → `091121`,
- missing reference → client-side `EbicsConfigurationException` without a round-trip.

Additionally `UploadValidationTests` for the H005 `AdminOrderType` path.

## Related docs

- [VEU orders (server)](../server/veu-orders.md) — the server-side implementation and the state machine
- [Upload API](upload.md) · [Download API](download.md) — the families the VEU fits into
- [Order/BTF coverage matrix](../server/order-coverage-matrix.md) — server and client availability
- [ADR-0030](../adr/0030-defaults-und-clientseitige-veu-anbindung.md) · [ADR-0020](../adr/0020-veu-orders.md)
