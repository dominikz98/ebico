# Domain model: Bank / Partner / User / Subscriber (H003/H004/H005)

The first hand-written domain primitives in `EBICO.Core`: type-safe
identifiers, the subscriber lifecycle and the authorisation/signature classes.
Until now, `HostID`/`PartnerID`/`UserID`/`SystemID` existed only as raw
`string` fields on the [generated bindings](xsd-bindings.md) (e.g.
`StaticHeaderType.HostId`). Issue **#16** (Milestone M1),
convention: [ADR-0007](../adr/0007-domaenen-value-objects-record-struct.md).

> **Scope:** Deliberately only identity, state and authorisations. Persistence,
> crypto and key management do not belong here — they follow in M2 (crypto) and M3
> (server/master data, among others #30). Order/BTF types are still free strings here;
> the typed model comes in M5.

## Building blocks

All under `src/EBICO.Core/Domain/` (namespace `EBICO.Core.Domain`):

| Building block | Location | Purpose |
|---|---|---|
| `HostId`, `PartnerId`, `UserId`, `SystemId` | `Identifiers.cs` | type-safe ID value objects (`readonly record struct`) |
| `EbicsIdentifier` | `EbicsIdentifier.cs` | shared validation against the schema pattern (internal) |
| `SubscriberState` (enum) | `SubscriberState.cs` | lifecycle: `New`/`Initialized`/`Ready`/`Suspended` |
| `SignatureClass` (enum) + `SignatureClassExtensions` | `SignatureClass.cs` | signature class `E`/`A`/`B`/`T` + transport-vs-bank classification |
| `SubscriberPermission` | `SubscriberPermission.cs` | authorisation: order type × signature class |
| `Address`, `BankAccount` | `Address.cs`, `BankAccount.cs` | customer address / account (delivered by HTD/HKD, #41) |
| `Bank`, `Partner`, `Subscriber` | `Bank.cs`, `Partner.cs`, `Subscriber.cs` | lean, immutable aggregates |
| `EbicsDomainException` (+ derived) | `DomainExceptions.cs` | validation errors of the domain model |

## Identifiers

All four IDs share the same schema restriction — **1–35 characters from
`[a-zA-Z0-9,=]`** — and are therefore checked via a shared, internal validator
(`EbicsIdentifier`, source-generator regex). As value objects they are four
**distinct** types: a `UserId` cannot accidentally be passed where a
`PartnerId` is expected.

| ID | Meaning | Mandatory | Constraint |
|---|---|---|---|
| `HostId` | bank/server endpoint (`HostID`) | yes | `[a-zA-Z0-9,=]{1,35}` |
| `PartnerId` | customer (`PartnerID`) | yes | `[a-zA-Z0-9,=]{1,35}` |
| `UserId` | subscriber (`UserID`) | yes | `[a-zA-Z0-9,=]{1,35}` |
| `SystemId` | technical system (`SystemID`) | optional (multi-user) | `[a-zA-Z0-9,=]{1,35}` |

```csharp
var host = HostId.Create("BANKDE01");          // wirft InvalidEbicsIdentifierException bei ungültig

if (UserId.TryCreate(input, out var user))     // nicht-werfende Variante
{
    // user.Value ist garantiert valide
}

HostId.Create("A,B=C");                          // ok: Komma und Gleichheitszeichen sind erlaubt
HostId.Create("AB CD");                          // InvalidEbicsIdentifierException (Leerzeichen)
HostId.Create(new string('X', 36));              // InvalidEbicsIdentifierException (zu lang)
```

> **Caveat (struct-related):** `default(HostId)` / `new HostId()` bypasses the factory and
> carries `Value == null`. Valid instances arise exclusively via
> `Create`/`TryCreate`. Value equality holds per type: two `HostId` with the same
> `Value` are equal.

## Authorisations — transport vs. bank signature

`SignatureClass` is the version-independent domain counterpart to the generated
`AuthorisationLevelType` (identical across H003/H004/H005). The central distinction
is **transport (`T`)** versus **bank-technical/authorising (`E`/`A`/`B`)**:

| Class | Meaning | Authorising? |
|---|---|---|
| `E` | single signature | yes (`IsBankTechnical`) |
| `A` | first signature | yes (`IsBankTechnical`) |
| `B` | second signature | yes (`IsBankTechnical`) |
| `T` | transport signature (submission only, no authorisation) | no (`IsTransportOnly`) |

```csharp
SignatureClass.T.IsTransportOnly();    // true
SignatureClass.E.IsBankTechnical();    // true

var perm = new SubscriberPermission("CCT", SignatureClass.T);  // CCT nur einreichen, nicht freigeben
perm.IsTransportOnly;                                          // true
```

A `Subscriber` bundles its authorisations and answers from them:
`CanAuthorize(orderType)` (holds a bank-technical authorisation) or
`IsTransportOnlyFor(orderType)` (transport only for this order type).

## Subscriber states

The lifecycle of a subscriber. Transitions are encapsulated in
`Subscriber.Transition(SubscriberState)`; disallowed transitions throw
`InvalidSubscriberStateTransitionException`. Since the aggregate is immutable,
`Transition` yields a **new** instance.

| State | Meaning |
|---|---|
| `New` | created, no keys sent yet (no INI/HIA) |
| `Initialized` | signature key sent via INI, not yet operational |
| `Ready` | fully onboarded and activated |
| `Suspended` | locked, until reactivation |

Permitted transitions:

| from → to | permitted |
|---|---|
| `New` → `Initialized` | ✅ |
| `Initialized` → `Ready` | ✅ |
| `New`/`Initialized`/`Ready` → `Suspended` | ✅ |
| `Suspended` → `Ready` (reactivation) | ✅ |
| everything else (incl. self-transition, skipping) | ❌ → exception |

```csharp
var subscriber = new Subscriber(host, partner, user);   // State = New
subscriber = subscriber.Transition(SubscriberState.Initialized)
                       .Transition(SubscriberState.Ready);
subscriber.Transition(SubscriberState.New);             // InvalidSubscriberStateTransitionException
```

## Aggregates

Lean and immutable (`sealed class`, get-only properties), analogous to
`EbicsVersionInfo`:

- `Bank` — identity `HostId`, optional `Name` (HPD `Institute`), supported `EbicsVersion`s
  (default: all) and optional `Url` (HPD access URL, #41).
- `Partner` — identity (`HostId`, `PartnerId`), optional `Name`; belongs to **exactly one**
  bank and groups its subscribers. The scoping per bank enables
  multi-tenancy (the same `PartnerId` string denotes different customers at different banks)
  and was added in the server layer (#30). It additionally carries an optional
  `Address` and `BankAccount`s (delivered by HTD/HKD, #41).
- `Subscriber` — identity via the triple (`HostId`, `PartnerId`, `UserId`), optional
  `SystemId` (technical subscriber → `IsTechnicalSubscriber`), optional `Name` (delivered by
  HTD/HKD, #41), `SubscriberState` and `SubscriberPermission`s. Authorisations are updated
  immutably: `WithPermission` / `WithoutPermissionsFor` / `WithPermissions`
  each yield (like `Transition`) a new instance (the `Name` is preserved in the process).

The server-side CRUD management of these aggregates (incl. referential integrity and
cascading deletion) is described by the [master-data management](../server/master-data.md) (#30).

## EBICS version relation

IDs (pattern/length) and signature classes (`E`/`A`/`B`/`T`) are identical across **H003, H004 and
H005**; the domain model is therefore version-independent. Only the
XML namespaces of the schemas differ — that concerns the
[bindings](xsd-bindings.md), not this model.

## Tests

`tests/EBICO.Tests/Domain/` (Tier A, CI-safe, without proprietary samples):

- `IdentifierTests` — valid values & boundary lengths (1/35), invalid ones (empty, too long,
  illegal characters, `null`), `TryCreate`, value equality, `default` caveat, all four types.
- `SignatureClassTests` — `IsTransportOnly`/`IsBankTechnical`, partition over all values.
- `SubscriberTests` — permitted/disallowed state transitions, identity/permission preservation,
  `SystemId`/technical subscriber, `CanAuthorize`/`IsTransportOnlyFor`.
- `BankPartnerTests` — construction, default versions, identity.

## Related

- [ADR-0007 — Domain value objects as `readonly record struct`](../adr/0007-domaenen-value-objects-record-struct.md)
- [Version dispatch](version-dispatch.md) — the `EbicsVersion` abstraction that `Bank` builds on
- [XSD bindings](xsd-bindings.md) — the generated types with the raw ID fields and `AuthorisationLevelType`
