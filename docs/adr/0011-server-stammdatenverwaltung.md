# 0011 — Server master-data management (manager over store, admin API)

- Status: accepted
- Date: 2026-07-10

## Context

The server scaffolding (#25, [ADR backlog](README.md)) brought the read/write
`IEbicsStateStore` with an in-memory default implementation, but only with
`Get*`/`Register*` (upsert). Issue #30 (M3) requires real **master-data
management**: full CRUD, authorisations per order type/BTF and
**multi-bank/multi-tenancy** capability. Several decisions must be made here: where
referential integrity lives, how partners are modelled tenant-scoped, what happens
to dependent objects on deletion and how the CRUD is exposed to the outside. The ADR
backlog listed "persistence of the server state (in-memory default, pluggable store)
— M3/M4" as open.

## Decision

1. **Two-layered: manager over store.** The `IEbicsStateStore` stays a "dumb"
   persistence abstraction (get/register/remove/scoped queries) without business
   rules. On top sits a new `IMasterDataManager` that enforces referential integrity
   and cascading deletion and encapsulates the permission/lifecycle mutation. The
   admin API and (later) onboarding handlers go exclusively through the manager.
2. **Partners scoped per bank.** `Partner` now carries `HostId`; the store keys
   partners by (`HostId`, `PartnerId`) rather than globally. The same `PartnerId`
   string denotes different customers at different banks (multi-tenancy).
3. **Cascading deletion.** Deleting a bank removes its partners and subscribers,
   deleting a partner removes its subscribers. (The alternative "forbid deletion
   while dependents exist" was rejected in favour of simpler emulator operation.)
4. **Unauthenticated HTTP admin API.** `MapEbicoAdminApi` maps a nested REST/JSON
   surface over the manager. It is deliberately **without** AuthN/AuthZ — fitting for
   local emulator/test operation (like Azurite). AuthN/AuthZ is a later server issue.
5. **In-memory default, pluggable.** The state stays in-memory by default; a
   persistent store can be plugged in via a `TryAddSingleton` override without
   changing callers (the interface is prepared async). This addresses the backlog
   item "persistence of the server state".

## Consequences

- **Clear separation of responsibilities:** store = persistence, manager =
  invariants. A later persistent store inherits the business rules automatically
  because they live in the manager.
- **Referential integrity only via the manager.** Anyone who writes to the store
  directly bypasses the checks — this is accepted (test stubs, seeding), the public
  paths go through the manager.
- **Cascades can silently delete data.** Intended for an emulator; the admin API
  makes the behaviour documented and transparent.
- **Security:** the admin API must not be exposed on untrusted networks.
- **Error model provisional:** the manager throws typed exceptions
  (`UnknownBank/Partner/Subscriber`), which the admin API maps to HTTP status. The
  central `EbicsResult<T>`/return-code model remains reserved for #36 (M4).

## Alternatives

- **CRUD directly in the store (no manager):** mixes business rules with persistence
  and makes swapping the store risky — rejected.
- **Global partners (only `PartnerId`):** minimal intervention, but not EBICS-faithful
  for multi-bank scenarios (customer numbers are bank-specific) — rejected.
- **State layer only, no admin API:** would leave the CRUD without an operable
  surface until #53 (M7); a lean admin API makes the emulator usable immediately —
  hence included.
