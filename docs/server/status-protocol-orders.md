# Server: Status & Protocol Orders (HAC/HAA/HTD/HKD/HPD/PTK)

> Implementation of **Issue #41** (Milestone M5 — Server: Orders & BTF). This page describes the
> **administrative/technical download orders** on top of the [download transaction](download-transaction.md):
> HTD/HKD/HAA/HPD are generated from the master data, HAC/PTK are pure projections over the
> [event store `IEventLog`](event-log.md).
>
> Deliberately **included**: the six order types — **HTD** (customer/subscriber data of the subscriber), **HKD**
> (customer data including all subscribers), **HAA** (available download order types), **HPD** (bank parameters),
> **HAC** (Customer Protocol, XML) and **PTK** (Customer Protocol, text); the **domain extension**
> (`Address`, `BankAccount`, `Partner.Address`/`Partner.Accounts`, `Bank.Url`, `Subscriber.Name`) including
> the admin API; the version-aware core builders (`SubscriberInfoContentBuilder`, `HacProtocolBuilder`,
> `PtkProtocolBuilder`); two pluggable `IDownloadOrderProcessor` (`SubscriberInfoDownloadProcessor`,
> `CustomerProtocolDownloadProcessor`); the **strict authorisation check** (as with the BTF orders).
> Deliberately **not yet**: the **wire-exact** HAC format (camt.086/pain.002 — proprietary/no schema, here
> a plausible custom projection); binding fields that are not modelled (order/transfer format, amount limits,
> authorisation level, X.509 parameters, account usage restrictions); the **X002 signature** of the response (M4);
> the distributed EU (HVE/HVD/…, [#42](../ticket-overview.md)).

## Purpose

After onboarding (INI/HIA/HPB) an EBICS client needs the **master and status data** of the bank: which
customer/subscriber data and accounts are on file (HTD/HKD), which order types can be retrieved (HAA), which
bank parameters apply (HPD) and what happened to its orders (HAC/PTK, "Customer Protocol"). All
six are bank→client **downloads**, so they run over the existing
[download transaction](download-transaction.md) — but unlike the account statements ([#40](statement-orders.md))
they remain classic **AdminOrderTypes** in H005 (no BTF service, see [BTF framework](btf-framework.md)).
#41 attaches two content sources to the generate-on-demand point of the engine: the **master data**
(`IMasterDataManager`) for HTD/HKD/HAA/HPD and the **event store** for HAC/PTK.

## Submission conventions & routing

The order codes are submitted **directly** (no `FDL`/`BTD`, no FileFormat, no BTF service);
`BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, null, null)` passes the raw code through:

| Version | Convention | Example | Resolution |
| --- | --- | --- | --- |
| H005 | `AdminOrderType` **direct** | `AdminOrderType=HTD` | → `HTD` |
| H003/H004 | classic `OrderType` **direct** | `OrderType=HTD` | → `HTD` |

Besides `FDL`/`BTD` and the statement codes, the routing detection `DownloadTransactionEngine.IsDownloadOrderType`
now also knows the status/protocol codes (`StatusProtocolOrderTypes.IsStatusProtocolOrderType`:
HTD/HKD/HAA/HPD/HAC/PTK). The code is — as with all downloads — resolved **before** the authorisation check
and passed on as the queue/generation key.

Generation is spread across **several** `IDownloadOrderProcessor`: the engine now takes
`IEnumerable<IDownloadOrderProcessor>` and picks the first matching `CanProcess`. Registered are
`StatementDownloadProcessor` (#40), `SubscriberInfoDownloadProcessor` (HTD/HKD/HAA/HPD) and
`CustomerProtocolDownloadProcessor` (HAC/PTK).

## Flow

Resolution, authorisation and provisioning happen in the **initialisation**; transfer/receipt work
unchanged on the generated payload (see [download transaction](download-transaction.md)):

| Step | Action |
| --- | --- |
| 1. Resolve | effective order code = raw admin/order code; for HAC/PTK extract the optional `DateRange` |
| 2. Authorise | `Subscriber.HasPermissionFor(code)` — otherwise `090003` (authorisation required, no auto-grant) |
| 3a. Dequeue | try the queue by the code (admin-seedable raw payload takes precedence) |
| 3b. Generate | HTD/HKD/HAA/HPD from `IMasterDataManager`; HAC/PTK from `IEventLog` (customer-visible, per customer) |
| 4. Send | compress (`EbicsCompression`) → E002 encrypt → segment → segment 1 + `NumSegments` |

The generated payload is **plaintext** (XML for HTD/HKD/HAA/HPD/HAC, text for PTK, **no** ZIP); the
encryption/segmentation is done exclusively by the engine. Retrieving HTD/HKD/HAA/HPD writes a
customer-visible `OrderAccepted` event; the **HAC/PTK retrieval** only writes an `Internal` event (no
additional customer-visible `OrderAccepted`). The `DownloadStarted`/`DownloadCompleted` lifecycle events
of the transaction remain — as with every download — customer-visible; a protocol retrieval is therefore
itself visible in later protocols.

### Master data source

HTD/HKD populate `PartnerInfo` (address, bank info, accounts, order info) and `UserInfo` (UserID/name,
authorisations) from the extended domain model: `Partner.Address`/`Partner.Accounts`, `Subscriber.Name`
and the subscriber permissions. HPD draws `AccessParams` (URL/institute/HostID) from `Bank`
(`Url`/`Name`/`HostId`) and `ProtocolParams/Version` from `Bank.SupportedVersions` (+ fixed crypto versions
X002/E002/A005/A006). HAA lists the downloadable order types of the subscriber.

### Example — HTD (H005, abridged)

```xml
<HTDResponseOrderData xmlns="urn:org:ebics:H005">
  <PartnerInfo>
    <AddressInfo><Name>Acme GmbH</Name><City>Berlin</City><Country>DE</Country></AddressInfo>
    <BankInfo><HostID>EBICOHOST</HostID></BankInfo>
    <AccountInfo ID="ACC1" Currency="EUR" Description="Main account">
      <AccountNumber international="true">DE89370400440532013000</AccountNumber>
      <BankCode international="true">COBADEFFXXX</BankCode>
    </AccountInfo>
    <OrderInfo><Service><ServiceName>EOP</ServiceName><MsgName>camt.053</MsgName></Service> … </OrderInfo>
  </PartnerInfo>
  <UserInfo>
    <UserID Status="5">USER01</UserID><Name>Alice</Name>
    <Permission><AdminOrderType>HTD</AdminOrderType></Permission>
  </UserInfo>
</HTDResponseOrderData>
```

In H003/H004 `OrderInfo`/`Permission` carry the classic `OrderType`/`OrderTypes` instead of
`AdminOrderType`/`Service`, and HAA lists `OrderTypes` (codes) instead of `Service` (BTF).

### Example — HAC (Customer Protocol, custom projection, abridged)

```xml
<HACResponseOrderData xmlns="urn:org:ebics:H005">
  <ProtocolEntry sequence="7" timestamp="2026-07-15T10:00:00Z" severity="Info">
    <OrderType>CCT</OrderType>
    <ReturnCode symbolic="EBICS_OK">000000</ReturnCode>
    <Message>Download started (1 segment(s), order type HTD).</Message>
  </ProtocolEntry>
</HACResponseOrderData>
```

PTK renders the same projection as plaintext, one line per event
(`2026-07-15T10:00:00Z [Info] CCT 000000 (EBICS_OK): …`).

## Return codes & error cases

| Situation | Return code | Placement |
| --- | --- | --- |
| Success (segment 1 delivered) | `000000` EBICS_OK | Header + Body |
| No authorisation for the order type | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | Body |
| Subscriber not `Ready`/unknown | `091002` EBICS_INVALID_USER_OR_USER_STATE | Body |
| Master data not found (bank/partner) | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | Body |

The remaining transaction/segment codes come unchanged from the
[download transaction](download-transaction.md).

### ⚠️ Spec caveats

- **HAC/PTK format.** EBICS defines HAC via a proprietary, version-dependent schema
  (camt.086/pain.002 derivation) that is not in the repo (licence). HAC here is a structurally plausible,
  self-describing **custom projection** of the events (`ProtocolEntry` per event), PTK a readable
  text form — both unverified against the official annexes.
- **Version-specific field mapping.** HTD/HKD/HAA/HPD are populated per version into the generated bindings;
  fields not held in the domain model (order/transfer format, `MaxAmount`, `AuthorisationLevel`,
  `X509Data`, account `UsageOrderTypes`) remain empty/omitted.
- **User `Status`.** The EBICS user status (`UserID/@Status`) is derived heuristically from the lifecycle
  (`Ready`→5, `Initialized`→2, otherwise 1).
- **HAA scope.** HAA lists the downloadable (BTF) order types of the subscriber (STA/C5x); purely
  administrative downloads are not listed as an HAA service.
- **Unsigned response.** X002 still deferred (M4), as with the download transaction.

## EBICS version mapping

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Order identity | `OrderType` direct (HTD/HKD/HAA/HPD/HAC/PTK) | `AdminOrderType` direct |
| HTD/HKD `OrderInfo` | `OrderType` (+ H004 `FileFormat`) / `TransferType` | `AdminOrderType` **or** `Service` (BTF) |
| HTD/HKD `Permission` | `OrderTypes` (list) | `AdminOrderType` **or** `Service` |
| HAA | `OrderTypes` (codes) | `Service` (BTF `RestrictedServiceType`) |
| HPD `ProtocolParams` | incl. `X509Data` (omitted) | without `X509Data` |
| HAC namespace | `http://www.ebics.org/H003` (H003) · `urn:org:ebics:H004` | `urn:org:ebics:H005` |
| PTK | present (legacy) | replaced by HAC |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; no proprietary fixtures):

- `Core/Administrative/StatusProtocolOrderTypesTests` — classification of the six codes.
- `Core/Administrative/SubscriberInfoContentBuilderTests` — HTD over H003/H004/H005 (string presence) +
  H005 round-trip (PartnerInfo/AccountInfo/OrderInfo/UserInfo), HKD (all subscribers), HAA (H005 `Service`
  vs. H004 `OrderTypes`), HPD (AccessParams/ProtocolParams, H005 + H003).
- `Core/Administrative/CustomerProtocolBuilderTests` — HAC namespace/entries (H005 + H003 legacy), empty
  protocol, PTK lines.
- `Domain/SubscriberInfoDomainTests` — `Partner.Address`/`Accounts`, `Bank.Url`, `Subscriber.Name` (incl.
  preservation across `Transition`/`WithPermission(s)`/`WithoutPermissionsFor`), `BankAccount` default currency.
- `Server/StatusProtocolDownloadTests` — **end-to-end** through the pipeline: HTD (H003/H004/H005), HKD, HAA
  (H005/H004), HPD, HAC/PTK (projection after a prior download), missing authorisation → `090003`.
- `Server/AdminApiIntegrationTests` — round-trip of bank `Url`, partner address/accounts, subscriber `Name`.

## Related documentation

- [Download transaction (initialisation + transfer + receipt)](download-transaction.md) — the send engine that #41 hooks into
- [Event/protocol store (`IEventLog`)](event-log.md) — source of the HAC/PTK projection (customer-visible, per customer)
- [Master data management](master-data.md) — banks/partners/subscribers incl. address/accounts/name/URL, authorisations, admin API
- [BTF framework (H005)](btf-framework.md) — admin vs. BTF order types, authorisation check
- [Download orders: account statements & reports](statement-orders.md) — the sister feature (#40) of the same engine
- [ADR-0019 (status & protocol orders)](../adr/0019-status-and-protocol-orders.md) — domain extension, HAC as an IEventLog projection
