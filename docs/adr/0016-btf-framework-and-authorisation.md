# 0016 — BTF framework & authorisation check

- Status: accepted
- Date: 2026-07-14

## Context

EBICS 3.0 (H005) replaces the classic three-letter order types (H003/H004) with the
generic admin order types `BTU`/`BTD` plus a **Business Transaction Format (BTF)** in
the `BTUOrderParams`/`BTDOrderParams` element. Until then the server treated the order
type as a free string, evaluated only the `AdminOrderType` for H005 and enforced **no**
authorisations (the engines only checked `State == Ready`). Issue #38 delivers the
framework for the concrete orders (#39–#43): a typed BTF model, BTF↔OrderType mapping
and an authorisation check per BTF. Two decisions were to be made here: (a) how strict
authorisation is and (b) how BTF authorisations are expressed.

## Decision

1. **Typed model in `EBICO.Core.Btf`.** `BusinessTransactionFormat`
   (`readonly record struct`, [ADR-0007](0007-domain-value-objects-record-struct.md))
   as a hand-written projection of the generated `ServiceType` binding; the generated
   `Schema/H005/*` types are mapped, not edited
   ([ADR-0006](0006-commit-generated-xsd-bindings.md)).

2. **Bridge via the order-type code.** The static `BtfOrderTypeCatalog` maps BTF ↔ the
   classic code. Authorisation uses a single **effective order-type key**: for H005 the
   BTF is resolved to its classic code, for H003/H004 the order type is used directly.
   `SubscriberPermission.OrderType` stays a string; the admin API and the
   `MasterDataManager` stay **unchanged**. (Rejected: a native
   `BusinessTransactionFormat` field on `SubscriberPermission` — a larger
   API/persistence surface with no benefit for the emulator.)

3. **Strict enforcement.** A `Ready` subscriber must hold a matching authorisation;
   otherwise `EBICS_AUTHORISATION_ORDER_TYPE_FAILED` (090003). There is **no** "empty
   permission set = everything allowed". (Rejected: lenient/opt-in — the emulator should
   reflect the real bank behaviour.)

4. **Static check logic, no new DI service.** `BtfOrderTypeCatalog` (Core) +
   `Subscriber.HasPermissionFor` (Core) are called inline in the engines; the engine
   constructors and the DI registration stay unchanged. The logic is directly
   unit-testable as a static helper.

## Consequences

- The catalogue seed is **representative and best-effort** (the authoritative External
  Code List is proprietary, [ADR-0003](0003-handling-proprietary-schemas.md));
  #39–#43 extend and verify it.
- Existing upload/download tests that created `Ready` subscribers without authorisations
  were migrated (matching authorisations seeded) — a consequence of strict enforcement.
- For H005 BTF-only services without a classic code, the `CanonicalKey` acts as the
  fallback key.
- `FUL`/`FDL` `FileFormat` → BTF and the evaluation of `SignatureFlag` remain reserved
  for later issues (see the [BTF framework docs](../server/btf-framework.md)).

## Alternatives

- **Native BTF in `SubscriberPermission`** (instead of the bridge) — rejected (see above).
- **Lenient/opt-in enforcement** — rejected (see above).
- **A dedicated `IOrderAuthorizationService` via DI** — rejected: unnecessary
  coupling/ctor ripple; the static variant is equally testable.
