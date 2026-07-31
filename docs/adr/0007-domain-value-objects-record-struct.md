# 0007 — Domain value objects as `readonly record struct`

- Status: accepted
- Date: 2026-06-22

## Context

Issue #16 introduces the first hand-written domain layer in `EBICO.Core`
(`Domain/`): identifiers (`HostId`, `PartnerId`, `UserId`, `SystemId`) and small
value objects (`SubscriberPermission`). These values are immutable, defined by their
content (value equality) and should be type-safe — a `UserId` must not accidentally
pass as a `PartnerId`, even though both only wrap a string.

So far the hand-written core uses `sealed class` exclusively with an explicit
constructor (e.g. `EbicsVersionInfo`); records have not appeared yet. A convention
must be set for the new, numerous small value objects.

## Decision

Domain **value objects** are implemented as `public readonly record struct`:

- private constructor + static factory `Create` (throwing) / `TryCreate`
  (non-throwing), so that only validated instances come into being;
- value equality and `GetHashCode` automatically from the record, no boilerplate;
- `struct` (allocation-free) for the typically small, short-lived IDs.

Larger **aggregates with identity** (`Bank`, `Partner`, `Subscriber`) stay with the
existing convention `sealed class` (immutable, get-only properties).

## Consequences

- Type safety and value semantics without manual `Equals`/`==`.
- **Caveat:** a `struct` always has an implicit parameterless constructor;
  `default(HostId)` / `new HostId()` thereby bypasses validation and carries
  `Value == null`. Convention: create instances only via `Create`/`TryCreate`; the
  `default` case is documented and covered by tests.
- New convention in the project — records are allowed for value objects from here on
  and are referenced in the docs ([domain-model.md](../protocol/domain-model.md)).

## Alternatives

- **`sealed class` for IDs too:** consistent with the existing code, but a lot of
  boilerplate (`Equals`/`GetHashCode`/`==`/`!=` by hand) and a heap allocation per ID.
- **A generic `EbicsId` type:** less code, but no type safety between the four ID
  kinds — rejected.
- **Raw `string`:** no validation at the source, no type safety — rejected.
