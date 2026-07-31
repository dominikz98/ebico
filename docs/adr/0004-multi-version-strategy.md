# 0004 — Multi-version strategy (H003/H004/H005)

- Status: accepted
- Date: 2026-06-21 (verified against the real schemas in M1)

## Context

EBICO supports three EBICS protocol versions — **H003** (2.4), **H004** (2.5),
**H005** (3.0). They differ in schemas, order/BTF model and partly in the
transaction flow. Core, server and connector must work version-dependently without
duplicating the logic threefold. The concrete design is verified against the real
schemas in M1 (see the constraint in `CLAUDE.md`).

## Decision (proposed)

- A central **`EbicsVersion`** abstraction in `EBICO.Core` (enum already present:
  `H003`/`H004`/`H005`) as the pivot point.
- **Version dispatch in Core** (issue #14): shared flows once, only the
  version-specific parts (schema bindings, order/BTF mapping, possibly
  segment/crypto details) behind version-selected implementations.
- **Separate XSD bindings per version** (issues #11–#13) in their own
  namespaces/folders, behind shared interfaces.

## Consequences

- One place where the target version is chosen (cf. connector DI:
  `o.Version = EbicsVersion.H005`).
- Shared logic stays testable version-independently; differences are local.
- **Verified in M1 (`accepted`):** the XSD bindings (#11–#13) are realised per
  version in their own namespaces — `EBICO.Core.Schema.{H003,H004,H005}` — while
  the genuinely shared schemas (xmldsig, HEV/H000, signature S001/S002) live
  **once** under `EBICO.Core.Schema.{XmlDsig,Hev,Signature.S001,Signature.S002}`
  (layout details: [../protocol/xsd-bindings.md](../protocol/xsd-bindings.md)).
  Open detail points about the flow (order of E002/A00x/X002, segment loop per
  version) are concretised together with the crypto/transport work (M2 onwards)
  against the annexes.

## Alternatives

- **Completely separate stacks per version:** maximum clarity, but massive
  duplication — rejected.
- **Newest only (H005) first, older ones later:** faster start, but contradicts the
  goal of broad version coverage — conceivable as a sequence, not as an architecture.
