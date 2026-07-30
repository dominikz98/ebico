# Suite: Master-Data Management (Banks / Partners / Subscribers)

> Implementation of **Issue #53** (Milestone M7 — Suite). Builds on the UI shell
> ([#52](ui-shell.md)) and is the write surface over the server-side
> **master-data management** from [#30](../server/master-data.md): it drives the
> `IMasterDataManager` (`EBICO.Server`) **in-process**, per
> [ADR-0009](../adr/0009-blazor-render-mode.md).

## Purpose

The `/stammdaten` page manages the emulator's master data: **banks** (credit institutions /
EBICS hosts), **partners** (customers) and **subscribers**. It covers creating, editing and
(cascading) deleting, makes the **subscriber state** visible/changeable and allows
editing a subscriber's **authorisations**. It replaces the read-only overview of the
shell (#52).

## Binding: in-process instead of HTTP

Instead of its own HTTP API (which does exist server-side as an admin API, see
[master-data.md](../server/master-data.md)) the Suite uses the state layer from `EBICO.Server`
**directly via DI** — the in-process binding envisaged by [ADR-0009](../adr/0009-blazor-render-mode.md).
To this end `EBICO.Suite` now references `EBICO.Server` (Suite → Server → Core).

```csharp
// Program.cs — server state in-process
builder.Services.AddSingleton<IEbicsStateStore, InMemoryEbicsStateStore>();
builder.Services.AddSingleton<IMasterDataManager, MasterDataManager>();
builder.Services.AddSingleton<SampleEmulatorStateProvider>();          // sample data + keys
builder.Services.AddScoped<IEmulatorStateProvider, EmulatorStateProvider>(); // live read model
…
var app = builder.Build();
await EmulatorStateSeeder.SeedAsync(app.Services);   // sample master data into the in-memory store
```

| Type | Role |
| --- | --- |
| `IMasterDataManager` (Server) | Write/management logic: CRUD, referential integrity, cascades, lifecycle, permissions |
| `EmulatorStateProvider` | Live read model: `GetBanks/Partners/SubscribersAsync` from the `IEbicsStateStore`; `GetKeysAsync` still from the sample data |
| `EmulatorStateSeeder` | fills the (empty) in-memory store at startup with sample master data (banks → partners → subscribers) |

The store is in-memory (state is lost on restart). Key material is not yet part
of the server store (a later M3/M4 issue), so `GetKeysAsync` still returns the
deterministic sample keys for the key view ([#55](schluessel-ansicht.md)).

## Render mode

The page itself is **Static SSR**; the three management areas are **interactive islands**
(`<BankManager @rendermode="InteractiveServer" />` etc.), the render mode is set at the embedding
site (ADR-0009, "interactivity per component"). Forms use plain Bootstrap with
`@bind`/`@onclick` and report results back via Bootstrap alerts — no exceptions in the UI.

## Consistency between the islands (#126)

The three islands are **independent components each with their own state copy**, but they write through
the same `IMasterDataManager` — and the relationships cascade. Without notification, therefore, every
mutation in one island silently invalidates the other two (a new bank is missing from the selection fields,
cascade-deleted rows remain as dead entries). For this there is
**`IMasterDataChangeNotifier`** ([ADR-0031](../adr/0031-stammdaten-inseln-aenderungsbenachrichtigung.md)):

```csharp
// Program.cs — singleton, like the stores behind it
builder.Services.AddSingleton<IMasterDataChangeNotifier, MasterDataChangeNotifier>();
```

Rules for every component that displays master data:

1. **Subscribe** in `OnInitializedAsync`, **release** the subscription in `Dispose`
   (`@implements IDisposable`).
2. After **every** successful mutation, `await Changes.NotifyChangedAsync()`.
3. In the handler, switch back to your own renderer via **`InvokeAsync`** — the notification
   arrives on the thread of the triggering circuit, not on your own.
4. After reloading, **check transient UI states**: an open form must no longer offer a deleted
   bank, a delete confirmation for a cascaded record is moot, a
   detail area without a record closes itself. Reloading alone only repairs the tables.

> **Whoever forgets the subscription goes stale silently again** — there is no guard for this other than the tests in
> `StammdatenIslandSyncTests`.

Because the notifier is a singleton, **multiple browser sessions** also converge: the state
behind it is shared process-wide, and so is the notification.

## Creating is not overwriting (#126)

The manager's `Save*` operations are **idempotent upserts**
([master-data.md](../server/master-data.md)) — correct for the API, but dangerous behind a
form called "create". A `SaveSubscriberAsync(new Subscriber(...))` onto an already
occupied identity reset the subscriber to `New` and discarded all authorisations, reported with
a green success message. The create paths therefore check the identity **beforehand**
(`GetBankAsync`/`GetPartnerAsync`/`GetSubscriberAsync`) and reject the collision with a pointer to
"edit" or "details". The edit paths are untouched — there overwriting *is* intended.

The multi-tenant semantics are preserved: the same `PartnerID` at a different bank or the same
`UserID` under a different partner is a **different** identity and is not counted as a collision.

## Sorting

The store returns dictionary order (`_banks.Values.ToArray()`), i.e. no guaranteed ordering —
in the UI, rows would jump to unpredictable places when creating/deleting. The components
therefore sort themselves (banks by `HostID`, partners by `(HostID, PartnerID)`, subscribers by
`(HostID, PartnerID, UserID)`, ordinal). The store deliberately stays "dumb" — sorting is a
presentation concern.

## Structure

| Component | Content | Operations |
| --- | --- | --- |
| `BankManager` | List of HostID / name / versions | Create, edit (HostID read-only), delete (**cascade**: partners + subscribers) |
| `PartnerManager` | List of HostID / PartnerID / name | Create (bank via dropdown), edit (name), delete (**cascade**: subscribers) |
| `SubscriberManager` | List of HostID / PartnerID / UserID / status / type + detail | Create (bank+partner via dropdown), change status, edit authorisations, delete |

Inputs are validated via `HostId/PartnerId/UserId/SystemId.TryCreate` (friendly message
instead of exception). When editing, the ID fields are locked (they are the store keys —
renaming = creating anew). Partners/subscribers are created via **dropdowns** from the existing
banks/partners, so that no orphaned records arise (the manager would reject
reference-violating creations with `UnknownBankException`/`UnknownPartnerException` anyway).

## Subscriber state

State transitions go through `IMasterDataManager.TransitionSubscriberAsync`, which validates the
lifecycle in `Subscriber.Transition`. The UI shows only the **permitted** transitions
of the current state as buttons:

| Current state | Actions |
| --- | --- |
| `New` | Initialise (→ `Initialized`), Suspend (→ `Suspended`) |
| `Initialized` | Activate (→ `Ready`), Suspend (→ `Suspended`) |
| `Ready` | Suspend (→ `Suspended`) |
| `Suspended` | Reactivate (→ `Ready`) |

An impermissible transition (`InvalidSubscriberStateTransitionException`) is caught defensively
and shown as an error alert.

## Authorisations

In a subscriber's detail area, authorisations (order type/BTF × signature class
`E`/`A`/`B`/`T`) can be added/removed as rows; "save authorisations" replaces the entire
set via `SetPermissionsAsync`. OrderType/BTF is currently a free string (typed model
→ M5).

## EBICS version reference

Identities (ID patterns/lengths) and signature classes are **identical across H003/H004/H005**; the
master-data management is thus version-independent. `Bank.SupportedVersions` (checkboxes in the
bank form) holds the offered versions per host (default: all).

## Tests

`tests/EBICO.Tests/Suite/` (bUnit + xUnit v3 + AwesomeAssertions; the component tests wire up
the **real** `MasterDataManager` via an `InMemoryEbicsStateStore`):

- `EmulatorStateProviderTests` — the read-model bridge returns store content and reflects
  live mutations; `GetKeysAsync` delegates to the sample keys.
- `EmulatorStateSeederTests` — the seeder creates the sample master data in order and is
  idempotent.
- `BankManagerTests` — render, create, invalid HostID → warning, delete.
- `PartnerManagerTests` — create via bank dropdown, "without bank" lock, delete.
- `SubscriberManagerTests` — create via dependent dropdowns, state transition, add/save
  authorisation, delete.
- `MasterDataChangeNotifierTests` — the broadcast contract: every subscriber is reached, `Dispose`
  really unsubscribes (and is idempotent), a failing subscriber does not stop the others.
- `StammdatenIslandSyncTests` — the #126 regression. Multiple components in **one**
  `BunitContext` share the DI container and thus the store and notifier, which reproduces the island layout of the page:
  a new bank appears in both selection fields (even in an *already open*
  form), a deleted bank disappears from them, cascades clear the sibling tables, a
  detail area over a cascaded subscriber closes itself, and the tables are sorted.
- `StammdatenCreateCollisionTests` — creating onto an occupied identity is rejected and leaves
  status/authorisations untouched; editing still saves; the same `PartnerID`/`UserID` at
  a different bank/different partner remains allowed.

## Related

- [UI shell & navigation](ui-shell.md)
- [Server: master-data management (#30)](../server/master-data.md) — the manager/store layer used
- [Domain model](../protocol/domain-model.md) — aggregates, IDs, authorisations, states
- [ADR-0009 — Blazor render mode (in-process state)](../adr/0009-blazor-render-mode.md)
- [ADR-0031 — Change notification between the master-data islands](../adr/0031-stammdaten-inseln-aenderungsbenachrichtigung.md)
