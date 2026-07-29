# Server: Master data management (banks / partners / subscribers)

> Implementation of **Issue #30** (Milestone M3 — Server: Key Management). This page
> describes the **CRUD management of the server state** (banks, partners/customers,
> subscribers), the **authorisations per order type/BTF** and the
> **multi-bank/multi-tenancy** capability — plus the accompanying, deliberately
> unauthenticated **HTTP admin API**.
>
> Deliberately **included**: full CRUD, referential integrity, cascading
> deletion, tenant-scoped queries, permission/lifecycle mutation, REST/JSON admin API.
> Deliberately **not yet**: AuthN/AuthZ of the admin API (a later server issue), a typed
> BTF/order type model (→ M5, currently a free-form string), server-side key material on the
> subscriber (later M3/M4 issues), a persistent store (in-memory remains the default), a Suite
> write UI (→ #53 / M7).

## Purpose

`EBICO.Server` is the EBICS emulator (conceptually like *Azurite* for Azure Storage).
The host scaffolding (#25, see [host.md](host.md)) already brought the authoritative,
read/write `IEbicsStateStore` with an in-memory implementation — but only with
`Get*` and `Register*` methods (upsert). #30 builds a real
**master data management** on top of it: create, read, update and **delete** with enforced
relationships, so that the later onboarding handlers (INI/HIA/HPB, M3/M4) and the
Suite management UI (#53, M7) build on a consistent state.

## Model & multi-tenancy

The state maps a **bank → partner → subscriber** hierarchy onto the
version-independent `EBICO.Core.Domain` aggregates (see
[domain model](../protocol/domain-model.md)):

| Aggregate | Identity | Meaning |
| --- | --- | --- |
| `Bank` | `HostId` | credit institution / EBICS host |
| `Partner` | (`HostId`, `PartnerId`) | customer of **one** bank (customer number) |
| `Subscriber` | (`HostId`, `PartnerId`, `UserId`) | subscriber of a customer |

**Multi-tenancy:** partners and subscribers are keyed **per bank**. The same
`PartnerId` string (e.g. `CUST01`) denotes *different* customers at different banks;
likewise the same `UserId` can exist at several banks. This lets any number of
banks each with their own customers/subscribers run isolated side by side.

> Compared to #25, `Partner` was extended with `HostId` and switched in the store from a global
> `PartnerId` key to the (`HostId`, `PartnerId`) key.

## CRUD & referential integrity

Two layers:

- **`IEbicsStateStore`** (persistence primitives) — stores/reads aggregates by identity,
  extended with `Remove*` and bank-scoped queries (`GetPartnersForBankAsync`,
  `GetSubscribersForBankAsync`, `GetSubscribersForPartnerAsync`). The store enforces **no**
  relationships — it is deliberately "dumb" and pluggable (default: `InMemoryEbicsStateStore`).
- **`IMasterDataManager`** (management logic) — the actual master data API. It enforces:

| Operation | Rule |
| --- | --- |
| `SavePartnerAsync` | the bank (`HostId`) must exist, otherwise `UnknownBankException` |
| `SaveSubscriberAsync` | the bank **and** the partner must exist, otherwise `UnknownBankException` / `UnknownPartnerException` |
| `DeleteBankAsync` | **cascading**: first removes all subscribers and partners of the host, then the bank |
| `DeletePartnerAsync` | **cascading**: removes all subscribers of the partner, then the partner |
| `TransitionSubscriberAsync` | delegates to `Subscriber.Transition` (validates the lifecycle) |

`Save*` is an idempotent upsert (create **and** update). `Delete*` returns `bool`
(did the target exist?). Missing targets on mutations (permissions/state) throw
`UnknownSubscriberException`.

```csharp
await manager.SaveBankAsync(new Bank(HostId.Create("EBICOHOST"), "EBICO"));
await manager.SavePartnerAsync(new Partner(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), "Muster GmbH"));
await manager.SaveSubscriberAsync(new Subscriber(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), UserId.Create("USER01")));

// Deleting the bank removes partners + subscribers along with it.
await manager.DeleteBankAsync(HostId.Create("EBICOHOST"));
```

## Authorisations per order type/BTF

A subscriber bundles `SubscriberPermission`s (order type × `SignatureClass` `E`/`A`/`B`/`T`,
see [domain model](../protocol/domain-model.md)). Because the aggregate is immutable, the
new `Subscriber` mutators each return a new instance; the manager persists it:

| Manager method | Effect |
| --- | --- |
| `GrantPermissionAsync` | adds an authorisation (a duplicate per (order type, SignatureClass) is not held twice) |
| `RevokePermissionsAsync(orderType)` | removes **all** authorisations of an order type |
| `SetPermissionsAsync(permissions)` | replaces the entire set (duplicates are merged) |

> **Order type/BTF:** `SubscriberPermission.OrderType` remains a string (e.g. `"CCT"`, `"STA"`), but
> is **enforced** since the [BTF framework (#38)](btf-framework.md): upload/download are only
> executed if the subscriber holds a matching authorisation (otherwise `090003`). For H005 the
> BTF service (`BTUOrderParams`/`BTDOrderParams`) is mapped to the classic code via the `BtfOrderTypeCatalog`
> and checked against that.

## Admin API (HTTP)

`MapEbicoAdminApi(prefix = "/admin")` maps a nested REST/JSON surface over the
`IMasterDataManager`. It is mapped in `Program.cs` in addition to the `/ebics` endpoint; the
path is configurable via `EbicoServerOptions.AdminApiPath`.

| Method & path | Effect | Success |
| --- | --- | --- |
| `GET /admin/banks` | all banks | 200 |
| `GET/PUT/DELETE /admin/banks/{hostId}` | read / upsert / delete bank (cascade) | 200 / 200 / 204 |
| `GET /admin/banks/{hostId}/partners` | partners of the bank | 200 |
| `GET/PUT/DELETE …/partners/{partnerId}` | read / upsert / delete partner (cascade) | 200 / 200 / 204 |
| `GET …/partners/{partnerId}/subscribers` | subscribers of the partner | 200 |
| `GET/PUT/DELETE …/subscribers/{userId}` | read / upsert / delete subscriber | 200 / 200 / 204 |
| `PUT …/subscribers/{userId}/permissions` | replace the authorisation set | 200 |
| `POST …/subscribers/{userId}/state` | lifecycle transition (`{"target":"Ready"}`) | 200 |
| `GET /admin/banks/{hostId}/keys` | public **bank keys** (fingerprints, PEM) | 200 |

Example — create a subscriber (after the bank + partner exist):

```http
PUT /admin/banks/EBICOHOST/partners/CUST01/subscribers/USER01
Content-Type: application/json

{ "systemId": null, "state": "New", "permissions": [ { "orderType": "CCT", "signatureClass": "E" } ] }
```

> **Extended master data (#41):** For the status/protocol orders
> ([status-protocol-orders.md](status-protocol-orders.md)) the upsert DTOs carry additional, optional
> fields: `Bank.url` (HPD access URL), `Partner.address` (`{name,street,postCode,city,region,country}`) and
> `Partner.accounts` (`[{iban,bic,holder,currency,description,id}]`, delivered by HTD/HKD) as well as
> `Subscriber.name` (subscriber name). All are backward compatible (default `null`/empty) and are returned
> again by the respective `GET`.

### Retrieving bank keys — the emulator's "bank letter" (#124)

`GET /admin/banks/{hostId}/keys` returns the **public** keys of the bank (`X00x`/`E00x`) from the
`IServerBankKeyStore` — per fingerprint (hex and letter format), version, key length and
`SubjectPublicKeyInfo` PEM. The pair is generated on first access, exactly as HPB would do it, and
stays stable afterwards. An unknown bank yields **404**.

```jsonc
{
  "hostId": "EBICOHOST",
  "authentication": {
    "purpose": "Authentication", "version": "X002", "keySizeBits": 2048,
    "fingerprint": "A1B2…",              // compare against HpbResult
    "fingerprintLetterFormat": "A1 B2 …", // rendering as on a bank letter
    "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n…"
  },
  "encryption": { "purpose": "Encryption", "version": "E002", /* … */ }
}
```

> **Why:** a client is meant to verify the fingerprints from the HPB response against an **independent**
> channel — with a real bank that is the bank letter. Against a separately hosted emulator this channel
> did not exist: HPB delivered the keys, but nobody could know them beforehand, so
> `HpbResult.FingerprintsVerified` inevitably stayed `false` there. In-process,
> `IServerBankKeyStore.SetAsync` remains the way to set a *known* pair (that is what the quickstart does).
> Private components are **not** exposed.

Error mapping:

| Situation | HTTP status |
| --- | --- |
| target not found (GET/DELETE/state on an unknown subscriber) | **404** |
| reference violation (partner without bank, subscriber without bank/partner) | **409** |
| invalid lifecycle transition | **409** |
| invalid ID (`HostID`/`PartnerID`/`UserID`) or enum (version/signature class/state) | **400** |

> **Ground rule vs. `/ebics`:** the admin API is an *ordinary* REST API and uses real
> HTTP status codes. This is deliberately different from the EBICS endpoint, which answers protocol/business
> errors with **HTTP 200** + a return code in the envelope (see [host.md](host.md)).

### ⚠️ Security & spec caveats

- **The admin API is unauthenticated.** It is intended for local emulator/test operation
  (like Azurite). Do not expose it in untrusted networks; AuthN/AuthZ is a
  later server issue.
- **No persistent store:** the default `InMemoryEbicsStateStore` loses the state on
  restart. A persistent store can be plugged in via a `TryAddSingleton` override (the interface
  is prepared for async). See [ADR-0011](../adr/0011-server-stammdatenverwaltung.md).
- **Referential integrity lives in the manager, not in the store.** Whoever writes to the store directly
  bypasses the checks — the admin API and onboarding handlers always go through the manager.

## EBICS version mapping

Identities (ID pattern/length) and signature classes (`E`/`A`/`B`/`T`) are identical across **H003, H004
and H005**; the master data management is therefore version-independent. `Bank.SupportedVersions`
holds the offered versions per host (default: all).

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; without proprietary fixtures):

- `Domain/SubscriberTests` — the new permission mutators (`WithPermission`/`WithoutPermissionsFor`/
  `WithPermissions`) including the dedup invariant and immutability.
- `Domain/BankPartnerTests` — `Partner` with `HostId`; the same `PartnerId` at different banks.
- `Server/InMemoryEbicsStateStoreTests` — CRUD, `Remove*`, bank-scoped queries, multi-tenancy isolation.
- `Server/MasterDataManagerTests` — CRUD happy path, referential integrity (negative cases),
  cascade deletion, permission grant/revoke/set, lifecycle, tenant isolation.
- `Server/AdminApiIntegrationTests` — E2E via `WebApplicationFactory<Program>`: round trips,
  404/409/400 mapping, cascade via HTTP, DTO JSON round trip.

## Related documentation

- [Hostable server scaffolding](host.md) — host, pipeline, return codes, the underlying state store
- [Domain model](../protocol/domain-model.md) — aggregates, IDs, authorisations/signature classes, states
- [UI shell & navigation](../suite/ui-shell.md) — the read-only Suite counterpart (`IEmulatorStateProvider`)
- [ADR-0011 — Server master data management](../adr/0011-server-stammdatenverwaltung.md)
- [ADR-0007 — Domain value objects](../adr/0007-domaenen-value-objects-record-struct.md)
