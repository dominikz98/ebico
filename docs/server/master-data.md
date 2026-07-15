# Server: Stammdatenverwaltung (Banken / Partner / Teilnehmer)

> Umsetzung von **Issue #30** (Milestone M3 — Server: Key Management). Diese Seite
> beschreibt die **CRUD-Verwaltung des Server-Zustands** (Banken, Partner/Kunden,
> Teilnehmer), die **Berechtigungen pro OrderType/BTF** und die
> **Mehr-Banken-/Mehr-Mandanten-Fähigkeit** — plus die dazugehörige, bewusst
> unauthentifizierte **HTTP-Admin-API**.
>
> Bewusst **enthalten**: vollständiges CRUD, referentielle Integrität, kaskadierendes
> Löschen, mandantenscharfe Abfragen, Permission-/Lebenszyklus-Mutation, REST/JSON-Admin-API.
> Bewusst **noch nicht**: AuthN/AuthZ der Admin-API (späteres Server-Issue), typisiertes
> BTF/OrderType-Modell (→ M5, aktuell freier String), serverseitiges Schlüsselmaterial am
> Teilnehmer (spätere M3/M4-Issues), persistenter Store (In-Memory bleibt Default), Suite-
> Schreib-UI (→ #53 / M7).

## Zweck

`EBICO.Server` ist der EBICS-Emulator (konzeptionell wie *Azurite* für Azure Storage).
Das Host-Grundgerüst (#25, siehe [host.md](host.md)) brachte bereits den autoritativen,
read/write `IEbicsStateStore` mit einer In-Memory-Implementierung — allerdings nur mit
`Get*`- und `Register*`-Methoden (Upsert). #30 baut daraus eine echte
**Stammdatenverwaltung**: Anlegen, Lesen, Ändern und **Löschen** mit erzwungenen
Beziehungen, damit die späteren Onboarding-Handler (INI/HIA/HPB, M3/M4) und die
Suite-Verwaltungs-UI (#53, M7) auf einem konsistenten Zustand aufsetzen.

## Modell & Mehr-Mandanten-Fähigkeit

Der Zustand bildet eine Hierarchie **Bank → Partner → Teilnehmer** auf den
versionsunabhängigen `EBICO.Core.Domain`-Aggregaten ab (siehe
[Domänenmodell](../protocol/domain-model.md)):

| Aggregat | Identität | Bedeutung |
| --- | --- | --- |
| `Bank` | `HostId` | Kreditinstitut / EBICS-Host |
| `Partner` | (`HostId`, `PartnerId`) | Kunde **einer** Bank (Kundennummer) |
| `Subscriber` | (`HostId`, `PartnerId`, `UserId`) | Teilnehmer eines Kunden |

**Mehr-Mandanten-Fähigkeit:** Partner und Teilnehmer sind **pro Bank** gekeyt. Derselbe
`PartnerId`-String (z. B. `CUST01`) bezeichnet an unterschiedlichen Banken *unterschiedliche*
Kunden; ebenso kann dieselbe `UserId` bei mehreren Banken existieren. So lassen sich beliebig
viele Banken mit je eigenen Kunden/Teilnehmern isoliert nebeneinander betreiben.

> Gegenüber #25 wurde `Partner` um `HostId` erweitert und im Store von einem globalen
> `PartnerId`-Key auf den (`HostId`, `PartnerId`)-Key umgestellt.

## CRUD & referentielle Integrität

Zwei Schichten:

- **`IEbicsStateStore`** (Persistenz-Primitiven) — speichert/liest Aggregate nach Identität,
  ergänzt um `Remove*` und bankscoped Abfragen (`GetPartnersForBankAsync`,
  `GetSubscribersForBankAsync`, `GetSubscribersForPartnerAsync`). Der Store erzwingt **keine**
  Beziehungen — er ist bewusst „dumm" und pluggbar (Default: `InMemoryEbicsStateStore`).
- **`IMasterDataManager`** (Verwaltungslogik) — die eigentliche Stammdaten-API. Sie erzwingt:

| Operation | Regel |
| --- | --- |
| `SavePartnerAsync` | Bank (`HostId`) muss existieren, sonst `UnknownBankException` |
| `SaveSubscriberAsync` | Bank **und** Partner müssen existieren, sonst `UnknownBankException` / `UnknownPartnerException` |
| `DeleteBankAsync` | **kaskadierend**: entfernt zuerst alle Teilnehmer und Partner des Hosts, dann die Bank |
| `DeletePartnerAsync` | **kaskadierend**: entfernt alle Teilnehmer des Partners, dann den Partner |
| `TransitionSubscriberAsync` | delegiert an `Subscriber.Transition` (validiert den Lebenszyklus) |

`Save*` ist idempotenter Upsert (Anlegen **und** Aktualisieren). `Delete*` liefert `bool`
(existierte das Ziel?). Fehlende Ziele bei Mutationen (Permissions/State) werfen
`UnknownSubscriberException`.

```csharp
await manager.SaveBankAsync(new Bank(HostId.Create("EBICOHOST"), "EBICO"));
await manager.SavePartnerAsync(new Partner(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), "Muster GmbH"));
await manager.SaveSubscriberAsync(new Subscriber(HostId.Create("EBICOHOST"), PartnerId.Create("CUST01"), UserId.Create("USER01")));

// Löschen der Bank entfernt Partner + Teilnehmer mit.
await manager.DeleteBankAsync(HostId.Create("EBICOHOST"));
```

## Berechtigungen pro OrderType/BTF

Ein Teilnehmer bündelt `SubscriberPermission`s (Auftragstyp × `SignatureClass` `E`/`A`/`B`/`T`,
siehe [Domänenmodell](../protocol/domain-model.md)). Da das Aggregat unveränderlich ist, liefern
die neuen `Subscriber`-Mutatoren jeweils eine neue Instanz; der Manager persistiert sie:

| Manager-Methode | Wirkung |
| --- | --- |
| `GrantPermissionAsync` | fügt eine Berechtigung hinzu (Duplikat je (OrderType, SignatureClass) wird nicht doppelt gehalten) |
| `RevokePermissionsAsync(orderType)` | entfernt **alle** Berechtigungen eines Auftragstyps |
| `SetPermissionsAsync(permissions)` | ersetzt die gesamte Menge (Duplikate werden zusammengefasst) |

> **OrderType/BTF:** `SubscriberPermission.OrderType` bleibt ein String (z. B. `"CCT"`, `"STA"`), wird
> aber seit dem [BTF-Framework (#38)](btf-framework.md) **erzwungen**: Upload/Download werden nur
> ausgeführt, wenn die Teilnehmerin eine passende Berechtigung hält (sonst `090003`). Bei H005 wird der
> BTF-Service (`BTUOrderParams`/`BTDOrderParams`) über den `BtfOrderTypeCatalog` auf den klassischen Code
> gemappt und dagegen geprüft.

## Admin-API (HTTP)

`MapEbicoAdminApi(prefix = "/admin")` mappt eine geschachtelte REST/JSON-Oberfläche über den
`IMasterDataManager`. Sie wird in `Program.cs` zusätzlich zum `/ebics`-Endpoint gemappt; der
Pfad ist über `EbicoServerOptions.AdminApiPath` konfigurierbar.

| Methode & Pfad | Wirkung | Erfolg |
| --- | --- | --- |
| `GET /admin/banks` | alle Banken | 200 |
| `GET/PUT/DELETE /admin/banks/{hostId}` | Bank lesen / upsert / löschen (Kaskade) | 200 / 200 / 204 |
| `GET /admin/banks/{hostId}/partners` | Partner der Bank | 200 |
| `GET/PUT/DELETE …/partners/{partnerId}` | Partner lesen / upsert / löschen (Kaskade) | 200 / 200 / 204 |
| `GET …/partners/{partnerId}/subscribers` | Teilnehmer des Partners | 200 |
| `GET/PUT/DELETE …/subscribers/{userId}` | Teilnehmer lesen / upsert / löschen | 200 / 200 / 204 |
| `PUT …/subscribers/{userId}/permissions` | Berechtigungsmenge ersetzen | 200 |
| `POST …/subscribers/{userId}/state` | Lebenszyklus-Übergang (`{"target":"Ready"}`) | 200 |

Beispiel — Teilnehmer anlegen (nachdem Bank + Partner existieren):

```http
PUT /admin/banks/EBICOHOST/partners/CUST01/subscribers/USER01
Content-Type: application/json

{ "systemId": null, "state": "New", "permissions": [ { "orderType": "CCT", "signatureClass": "E" } ] }
```

> **Erweiterte Stammdaten (#41):** Für die Status-/Protokoll-Orders
> ([status-protocol-orders.md](status-protocol-orders.md)) tragen die Upsert-DTOs zusätzliche, optionale
> Felder: `Bank.url` (HPD-Zugangs-URL), `Partner.address` (`{name,street,postCode,city,region,country}`) und
> `Partner.accounts` (`[{iban,bic,holder,currency,description,id}]`, von HTD/HKD ausgeliefert) sowie
> `Subscriber.name` (Teilnehmer-Name). Alle sind rückwärtskompatibel (Default `null`/leer) und werden vom
> jeweiligen `GET` wieder zurückgeliefert.

Fehlerabbildung:

| Situation | HTTP-Status |
| --- | --- |
| Ziel nicht gefunden (GET/DELETE/State auf unbekanntem Teilnehmer) | **404** |
| Referenzverletzung (Partner ohne Bank, Teilnehmer ohne Bank/Partner) | **409** |
| Ungültiger Lebenszyklus-Übergang | **409** |
| Ungültige ID (`HostID`/`PartnerID`/`UserID`) oder Enum (Version/Signaturklasse/State) | **400** |

> **Grundregel vs. `/ebics`:** Die Admin-API ist eine *gewöhnliche* REST-API und nutzt echte
> HTTP-Statuscodes. Das ist bewusst anders als der EBICS-Endpoint, der Protokoll-/Businessfehler
> mit **HTTP 200** + Returncode im Envelope beantwortet (siehe [host.md](host.md)).

### ⚠️ Sicherheit & Spec-Vorbehalte

- **Die Admin-API ist unauthentifiziert.** Sie ist für den lokalen Emulator-/Testbetrieb gedacht
  (wie Azurite). Nicht in nicht-vertrauenswürdigen Netzen exponieren; AuthN/AuthZ ist ein
  späteres Server-Issue.
- **Kein persistenter Store:** Der Default `InMemoryEbicsStateStore` verliert den Zustand beim
  Neustart. Ein persistenter Store ist via `TryAddSingleton`-Override einhängbar (das Interface
  ist async vorbereitet). Siehe [ADR-0011](../adr/0011-server-stammdatenverwaltung.md).
- **Referentielle Integrität liegt im Manager, nicht im Store.** Wer den Store direkt bespielt,
  umgeht die Prüfungen — die Admin-API und Onboarding-Handler gehen immer über den Manager.

## EBICS-Versionsbezug

Identitäten (ID-Pattern/-Länge) und Signaturklassen (`E`/`A`/`B`/`T`) sind über **H003, H004
und H005 identisch**; die Stammdatenverwaltung ist daher versionsunabhängig. `Bank.SupportedVersions`
hält je Host die angebotenen Versionen (Default: alle).

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; ohne proprietäre Fixtures):

- `Domain/SubscriberTests` — die neuen Permission-Mutatoren (`WithPermission`/`WithoutPermissionsFor`/
  `WithPermissions`) inkl. Dedup-Invariante und Unveränderlichkeit.
- `Domain/BankPartnerTests` — `Partner` mit `HostId`; gleicher `PartnerId` an verschiedenen Banken.
- `Server/InMemoryEbicsStateStoreTests` — CRUD, `Remove*`, bankscoped Abfragen, Mehr-Mandanten-Isolation.
- `Server/MasterDataManagerTests` — CRUD-Happy-Path, referentielle Integrität (Negativfälle),
  Kaskadenlöschung, Permission-Grant/Revoke/Set, Lebenszyklus, Mandanten-Isolation.
- `Server/AdminApiIntegrationTests` — E2E über `WebApplicationFactory<Program>`: Round-Trips,
  404/409/400-Abbildung, Kaskade via HTTP, DTO-JSON-Round-Trip.

## Verwandte Doku

- [Hostable Server-Grundgerüst](host.md) — Host, Pipeline, Returncodes, der zugrundeliegende State-Store
- [Domänenmodell](../protocol/domain-model.md) — Aggregate, IDs, Berechtigungen/Signaturklassen, Zustände
- [UI-Grundgerüst & Navigation](../suite/ui-shell.md) — das read-only Suite-Gegenstück (`IEmulatorStateProvider`)
- [ADR-0011 — Server-Stammdatenverwaltung](../adr/0011-server-stammdatenverwaltung.md)
- [ADR-0007 — Domänen-Value-Objects](../adr/0007-domaenen-value-objects-record-struct.md)
