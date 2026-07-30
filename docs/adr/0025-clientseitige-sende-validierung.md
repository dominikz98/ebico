# 0025 — Client-side send validation (authorisation/BTF) in the connector

- Status: accepted
- Date: 2026-07-20

## Context

The send pipeline of `EBICO.Connector` (see
[connector architecture](../connector/architecture.md)) provides for a validation
*"authorisation, BTF"* as **stage 1**. It was the only one not yet implemented as its own
building block: the checks lay scattered and ran *late* in the executors
(`UploadExecutor`/`DownloadExecutor`) — empty payload, segment size and the order-identity
resolution (`NormalizeOrderIdentity`) were reached only **after** loading the keys, and an
authorisation check did not exist client-side at all (every rejection cost a server
round-trip). Issue #44 (connector epic, M6) closes this gap.

To be decided: (a) where the stage is anchored, (b) how errors are expressed, (c) whether
and how a client-side "authorisation" is configured — and how that relates to the
**server-side** authorisation check from [ADR-0016](0016-btf-framework-und-berechtigung.md).

## Decision

1. **Static helper `RequestValidator` (`EBICO.Connector.Validation`), no DI service.** It
   is called as the first statement in `UploadExecutor.ExecuteAsync`/
   `DownloadExecutor.ExecuteAsync` — before any key I/O, crypto, serialisation and
   transport. Deliberately the same pattern as server-side
   ([ADR-0016](0016-btf-framework-und-berechtigung.md), point 4: static check logic instead
   of an `IOrderAuthorizationService`) and consistent with the existing static helpers
   (`UploadSupport`, `EncryptionE002`). The executors are the only choke point for the
   generic **and** all convenience handlers; that avoids duplication across the handler
   types. The formerly scattered checks and the `NormalizeOrderIdentity` logic were
   **moved** there (not duplicated) — the validator is thereby the sole authority for order
   identity + header tuple.

2. **Asymmetric error semantics.** Structural/BTF violations (order identity not
   resolvable, a code known in the catalogue in the wrong direction, empty upload payload,
   non-positive segment size) are programming/config errors →
   **`EbicsConfigurationException`** (consistent with the previous `NormalizeOrderIdentity`).
   An authorisation denial is a business result →
   **`EbicsResult<T>.Failure("090003", …)`** (`EBICS_AUTHORISATION_ORDER_TYPE_FAILED`),
   exactly the code the bank would return. The validator throws structural errors directly
   and otherwise returns a small outcome (`RequestValidation<TIdentity>`) which the executor
   translates into `Failure` or the identity used downstream.

3. **Opt-in allow-list, default off.** `EbicsConnectionOptions.AllowedOrderTypes`
   (normalised to an ordinal `IReadOnlySet<string>` on `EbicsConnection`) lists the allowed
   **classic** order-type codes. If set, the connector rejects a request with a non-listed
   **effective classic** code locally (fail-fast, no round-trip). The key is the effective
   classic code — consistent with ADR-0016 (H005 `CCT` matches `"CCT"`, not the wire code
   `"BTU"`); administrative codes (HTD/…) are subject to the list too. An **empty** list
   (default) skips the check.

4. **Deliberate divergence from the server side.** The server enforces **strictly** and
   explicitly rejects "empty permission set = everything allowed" (ADR-0016, point 3),
   because it reflects real bank behaviour. The client, conversely, is **opt-in/lenient by
   default**: it does not know the subscriber authorisations by itself, and the bank remains
   the authority. The allow-list is a pure up-front optimisation (save a round-trip, a clear
   error), not an enforcement instance.

## Consequences

- Malformed or (opt-in) unauthorised requests fail **before** any key I/O/crypto/transport
  — faster and without side effects. Existing E2E/tier-A tests stay green unchanged
  (allow-list default empty; the server-side 090003 rejection is still covered separately
  by E2E).
- Onboarding (INI/HIA/HPB) does not go through the executors and is therefore never
  validated here — the allow-list cannot block onboarding.
- The divergence (strict server-side vs. opt-in client-side) is documented; whoever wants
  client-side safeguarding opts in via configuration.
- `AllowedOrderTypes` is modelled as a get-only, initialised collection (options
  convention; avoids CA2227/CA1819 under `TreatWarningsAsErrors`).

## Alternatives

- **DI service `IEbicsRequestValidator`** — rejected: no runtime substitutability needed,
  unnecessary coupling/ctor ripple (the same rationale as ADR-0016).
- **Validation in the handlers** instead of in the executors — rejected: duplication across
  all convenience handlers and without the identity resolution (BTU/FUL/BTD/FDL).
- **Strict enforcement like server-side** (empty list = nothing allowed) — rejected: the
  client is not the authorisation authority and must not block anything without explicit
  configuration.
- **A `SubscriberPermission` list instead of string codes** — rejected: SignatureClass has
  no enforcement meaning client-side; string codes bind cleanly from configuration and are
  directly comparable with the effective resolution output.
