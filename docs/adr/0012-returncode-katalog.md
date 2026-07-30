# 0012 — EBICS return-code catalogue (modelling & placement)

- Status: accepted
- Date: 2026-07-13

## Context

EBICS responses carry a six-digit return code (technical in
`header/mutable/ReturnCode`, business in `body/ReturnCode`). Until issue #36 (M4)
there were two **deliberately provisional** models for it: a server-local
`EbicsReturnCode` with nine codes (scaffolding #25) and an `EbicsResult<T>` in the
connector (#46). Both referenced #36 in code as the place where the **central,
complete** catalogue would come into being. The ADR backlog listed "return-code
modelling (`EbicsResult<T>` vs. exceptions, catalogue)" as an open decision.

To be clarified: (1) where does the catalogue live? (2) how is it modelled? (3) how
are exceptions mapped onto it? (4) how are the proprietary EBICS annexes handled?

Constraint: `EBICO.Core` must not reference `EBICO.Server` (project dependencies
Connector→Core, Server→Core). The mapping, however, must know server-side exceptions
(`EBICO.Server.State.MasterData*`).

## Decision

- **Catalogue in `EBICO.Core.ReturnCodes`** (shared primitives for server **and**
  connector):
  - `EbicsReturnCode` as a `public readonly record struct` (`Code`, `SymbolicName`,
    `Kind`) with static fields per code and `const OkCode` — pattern like
    [ADR-0007](0007-domaenen-value-objects-record-struct.md);
  - `EbicsReturnCodeKind` (enum `Technical`/`Business`) controls header vs. body
    placement;
  - `EbicsReturnCodes` as a registry (`All`/`Get`/`TryFromCode`/`IsSuccess`) —
    modelled on `Crypto/KeyVersions`.
- **Mapping stays server-side** (`EBICO.Server.ReturnCodes.EbicsErrorMapper` +
  `IEbicsErrorMapper`), because it knows server exceptions and is pure request
  processing; the connector does not need it (its own exception/`EbicsResult`
  model). The catalogue (Core) is the central primitive; the mapping is server-local.
- **Central, unambiguous exception→code mapping:** handlers surface order-data errors
  via `OrderDataFault.Wrap` as a dedicated `EbicsOrderDataException`; the mapper maps
  this type (and the low-level crypto/format errors) to `090004`, and the
  domain/master-data errors to `091002`. Bare
  `ArgumentException`/`InvalidOperationException` deliberately map to `061099`
  (server error), not to a business code.
- **Handling the spec:** codes and symbolic names are interface constants and are
  included; descriptions are phrased in our own words (no copying of the annex text).
  Entries beyond the nine verified codes carry `⚠️ spec caveat` and must be verified
  against the official annexes.

## Consequences

- One place for all return codes; `EbicsResult.OkReturnCode` refers to
  `EbicsReturnCode.OkCode` instead of duplicating the literal `"000000"`.
- The freshly merged M3 handlers were decoupled: the duplicated `try/catch` blocks
  (guard list once in `OrderDataFault`) are gone; behaviour unchanged (order data →
  `090004`, identifier/state → `091002`), covered by the existing pipeline tests.
- The catalogue is intentionally more comprehensive than the running code; unused
  codes exist as constants (e.g. TX codes for the M4 transaction engine) and are
  clearly marked as unverified.
- Docs: [protocol/return-codes.md](../protocol/return-codes.md).

## Alternatives

- **Leave the catalogue in `EBICO.Server` and only extend it:** minimal intervention,
  but not truly "central" (the connector would keep its own model) — rejected.
- **Pull the mapper into `EBICO.Core` too:** fails on the dependency direction (Core
  must not see the server `MasterData*` exceptions); it would have forced moving those
  exceptions into Core — unnecessarily wide scatter, rejected.
- **Leave the handler `try/catch` unchanged, only extend the mapper:** would leave the
  duplicated guard list standing — rejected in favour of the one-off `OrderDataFault`
  encapsulation.
- **Commit the complete annex-1 text:** legally delicate (proprietary) — rejected;
  only codes/names as constants, our own short descriptions.
