# Server: BTF Framework (H005)

> Implementation of **Issue #38** (Milestone M5 — Server: Orders & BTF). This page describes the
> **foundation** for the concrete order implementations (#39–#42) and the coverage matrix (#43):
> the typed **Business Transaction Format** model (H005), the **mapping BTF ↔ classic
> OrderTypes** (H004) and the **per-BTF authorisation check** in the transaction engines.
>
> Deliberately **included**: the value object `BusinessTransactionFormat` (`EBICO.Core.Btf`) as a
> hand-written projection of the generated `ServiceType` binding; the static
> `BtfOrderTypeCatalog` with a representative best-effort seed; the extraction of the BTF service from
> `BTUOrderParams`/`BTDOrderParams` in the pipeline; the **strict** authorisation in the upload/download init
> (`EBICS_AUTHORISATION_ORDER_TYPE_FAILED`, 090003) via the existing `SubscriberPermission`s.
> Deliberately **not yet**: the concrete upload/download orders (CCT/CDD/STA/C5x, [#39](../ticket-overview.md)/#40),
> status/protocol orders (HAC/HTD/…, #41), the distributed signature (HVx, #42); the complete
> External Code List; the evaluation of `SignatureFlag` (per-BTF ES requirement) and of
> `FULOrderParams`/`FDLOrderParams` `FileFormat` (H004) → BTF.

## Purpose

In **EBICS 3.0 (H005)** the classic three-letter order type (H003/H004, e.g. `CCT`, `STA`)
is replaced by the generic admin order types **`BTU`** (upload) and **`BTD`** (download) plus a
**Business Transaction Format (BTF)**. The BTF describes functionally *what* is transferred, and
lives in the `BTUOrderParams`/`BTDOrderParams` element (`Service`). Up to #38 the server treated the
order type throughout as a **free string** and, for H005, evaluated only the `AdminOrderType`
(`"BTU"`/`"BTD"`) — the actual BTF service was ignored, and authorisations were never
enforced. This framework closes the gap.

## BTF parameter model

`EBICO.Core.Btf.BusinessTransactionFormat` (a `readonly record struct`, [ADR-0007](../adr/0007-domaenen-value-objects-record-struct.md))
maps the BTF parameters in typed form:

| Property | Origin (`ServiceType`) | Meaning |
| --- | --- | --- |
| `Service` (mandatory) | `ServiceName` | Service code (e.g. `SCT`, `SDD`, `EOP`) |
| `Option` | `ServiceOption` | Additional option (e.g. `COR`, `B2B`) |
| `Scope` | `Scope` | Scope (ISO country/issuer) |
| `Container` | `Container` (flag) | Container identifier `SVC`/`XML`/`ZIP` |
| `MessageName` | `MsgName` (Value) | Message name (e.g. `pain.001`, `camt.053`, `mt940`) |
| `MessageVariant` | `MsgName@variant` | ISO 20022 variant |
| `MessageVersion` | `MsgName@version` | ISO 20022 version |
| `MessageFormat` | `MsgName@format` | Encoding (e.g. `XML`) |

Conversion between model and generated binding: `FromSchema(ServiceType)`,
`TryFromBtfParams(BtfParamsTyp)`, `ToServiceType()`/`ToRestrictedServiceType()`. `CanonicalKey` yields
a deterministic key (e.g. `"SCT:pain.001:COR"`) for logging and as a fallback authorisation key.

## BTF ↔ OrderType mapping

`EBICO.Core.Btf.BtfOrderTypeCatalog` is the static equivalence table classic OrderType ↔ BTF.
It carries a **representative best-effort seed** of the common payment and account-statement orders; the
concrete orders (#39–#42) extend it, #43 documents the result as the
[order/BTF coverage matrix](order-coverage-matrix.md).

| OrderType | Direction | Service | Option | Container | MsgName | Description |
| --- | --- | --- | --- | --- | --- | --- |
| `CCT` | Upload | `SCT` | – | – | `pain.001` | SEPA Credit Transfer |
| `CIP` | Upload | `SCT` | `INST` | – | `pain.001` | SEPA Instant Credit Transfer |
| `CDD` | Upload | `SDD` | `COR` | – | `pain.008` | SEPA Direct Debit (CORE) |
| `CDB` | Upload | `SDD` | `B2B` | – | `pain.008` | SEPA Direct Debit (B2B) |
| `STA` | Download | `EOP` | – | `ZIP` | `mt940` | Account statement (SWIFT MT940) |
| `C53` | Download | `EOP` | – | `ZIP` | `camt.053` | Bank-to-Customer Statement |
| `C52` | Download | `STM` | – | `ZIP` | `camt.052` | Bank-to-Customer Account Report |
| `C54` | Download | `EOP` | – | `ZIP` | `camt.054` | Debit/Credit Notification |

- `TryGetBtf(orderType)` — classic code → BTF.
- `TryGetOrderType(btf)` — BTF → code (match on `Service` + `Option` + `MessageName` family; a
  seeded `camt.053` also matches an incoming `camt.053.001.08`).
- `ResolveOrderType(adminOrderType, btf)` — **effective authorisation key**: BTF present → mapped
  code (otherwise `CanonicalKey`); no BTF → `adminOrderType` (H003/H004: `FUL`/`FDL`; H005 without BTF: `BTU`/`BTD`).

## Per-BTF authorisation check

The check builds on the existing authorisation model ([master data](master-data.md),
[domain model](../protocol/domain-model.md)): `Subscriber` bundles `SubscriberPermission`s (OrderType ×
`SignatureClass`). New is the gate method `Subscriber.HasPermissionFor(orderType)` (holds *any*
authorisation for the order type — in contrast to `CanAuthorize`, which requires a bank-technical E/A/B class).

Flow in the upload/download init (`UploadTransactionEngine.BeginUploadAsync` /
`DownloadTransactionEngine.BeginDownloadAsync`), **after** the `Ready` check and — for the download —
**before** dequeuing the data:

1. Pipeline extracts the BTF (`BTUOrderParams`/`BTDOrderParams` → `Service`) into `EbicsRequestContext.Btf`.
2. `effectiveOrderType = BtfOrderTypeCatalog.ResolveOrderType(context.OrderType, context.Btf)`.
3. `subscriber.HasPermissionFor(effectiveOrderType)` → otherwise **`090003`**.

**Enforcement is strict** (see [ADR-0016](../adr/0016-btf-framework-und-berechtigung.md)): a
`Ready` subscriber **must** hold a matching authorisation; without an authorisation the order is rejected with
`090003` (no "empty set = everything allowed").

### Example: H005 BTU with BTF (`BTUOrderParams`)

```xml
<OrderDetails>
  <AdminOrderType>BTU</AdminOrderType>
  <BTUOrderParams>
    <Service>
      <ServiceName>SCT</ServiceName>
      <MsgName>pain.001</MsgName>
    </Service>
  </BTUOrderParams>
</OrderDetails>
```

This BTF (`SCT`/`pain.001`) is mapped to the classic OrderType **`CCT`**; the subscriber
needs a `CCT` authorisation.

## Return codes & error cases

| Situation | Return code | Placement |
| --- | --- | --- |
| Authorised (authorisation present) | (init continues, usually `000000`) | – |
| No matching authorisation for the (resolved) order type | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | Body |

The code `090003` already existed in the [return-code catalog](../protocol/return-codes.md), but before #38
was never triggered. All cases are answered with **HTTP 200** and the return code in the `ebicsResponse`.

### ⚠️ Spec caveats

- **Best-effort mapping.** The authoritative EBICS *BTF mapping / External Code List* is proprietary
  (EBICS SC) and is **not** committed to the repo ([license](../legal/ebics-licensing.md)). The
  seed follows the public list to the best of our knowledge; the exact Service/Option/MsgName codes
  are verified against the official list with the concrete orders (#39–#43).
- **Container value not round-trip capable.** The SVC/XML/ZIP value lives in the generated binding on
  an untyped attribute of the `Container` flag; the model reads it best-effort, `ToServiceType`
  writes only the presence of the flag (not the value). To be revisited once the attribute is verified against
  the annex.
- **Admin/technical OrderTypes remain admin OrderTypes.** `HAC`/`HAA`/`HTD`/`HKD`/`HPD`/`PTK` are
  deliberately **not** modelled as a BTF service (in H005 they remain `AdminOrderType`); they are the subject of #41.
- **`FUL` `FileFormat` → OrderType (upload, implemented in #39).** In H003/H004 the functional
  order type lives in `FULOrderParams/FileFormat`; `BtfOrderTypeCatalog.TryGetOrderTypeByFileFormat` /
  `ResolveUploadOrderType` map the MsgName family (e.g. `pain.001.001.09` → `CCT`) for the
  upload authorisation/processing (see [payment orders](payment-orders.md)). The
  **download** side (`FDL` `FileFormat`) remains reserved for **[#40](download-transaction.md)**; the
  option (CORE/B2B) is not derivable from the FileFormat alone (CDD default).
- **`SignatureFlag` (per-BTF ES requirement).** Whether a BTU order requires an ES is controlled per the spec by
  `BTUOrderParams/SignatureFlag`; this is separate from the plain OrderType authorisation and open
  (cf. [upload transaction](upload-transaction.md)).

## EBICS version mapping

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Order type | `OrderDetails/OrderType` (e.g. `FUL`/`FDL`, classic code) | `OrderDetails/AdminOrderType` (`BTU`/`BTD`) |
| Functional identity | in the OrderType or `FileFormat` | in the **BTF** (`BTUOrderParams`/`BTDOrderParams` → `Service`) |
| Authorisation key | OrderType string directly | BTF → classic code (catalog), otherwise fallback |

BTF is purely **H005**; H003/H004 carry no BTF service.

## Tests

`tests/EBICO.Tests/Core/Btf/` and `tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; request XML
from committed Core bindings, no proprietary fixtures):

- `BusinessTransactionFormatTests` — construction/validation, `FromSchema`↔`ToServiceType` round-trip,
  `CanonicalKey`, `TryFromBtfParams`, value equality.
- `BtfOrderTypeCatalogTests` — round-trips per seed entry, `TryGetOrderType` matching (incl. MsgName
  family), `ResolveOrderType` (H004 OrderType, H005 with/without BTF, unmapped fallback).
- `SubscriberTests` — `HasPermissionFor` (every signature class counts).
- `BtfAuthorizationTests` — end-to-end via the pipeline: H005 BTU with mapped BTF → `Ok` (with
  matching authorisation) or **`090003`** (without); H004 `FUL` without authorisation → `090003`; H005 without
  BTF → fallback to `BTU` authorisation; download BTD analogously (`C53`).

## Related documentation

- [Order/BTF coverage matrix](order-coverage-matrix.md) — consolidated overview of all order types × version × status (#43)
- [Upload transaction](upload-transaction.md) / [Download transaction](download-transaction.md) — the engines where the check plugs in
- [Master data management](master-data.md) — `SubscriberPermission`, grant/revoke, admin API
- [Domain model](../protocol/domain-model.md) — subscriber aggregate, signature classes
- [EBICS return-code catalog](../protocol/return-codes.md) — `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED
- [ADR-0016 (BTF framework & authorisation)](../adr/0016-btf-framework-und-berechtigung.md) — decisions *strict* & *bridge via OrderType code*
- [License & repo policy](../legal/ebics-licensing.md) — proprietary schemas/External Code List
