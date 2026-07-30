# 0006 — Commit generated XSD bindings (Option B)

- Status: accepted
- Date: 2026-06-21

## Context

M1 generates C# bindings from the EBICS XSDs (issues #11–#13).
[ADR-0003](0003-umgang-mit-proprietaeren-schemas.md) excluded the XSDs themselves
from the repo and **left the follow-up question open**: whether the generated
bindings count as a *derivative work* and may be committed. The options are
described in [../legal/ebics-licensing.md](../legal/ebics-licensing.md):

- **(A)** Do not commit the bindings, generate them from local XSDs at build time.
- **(B)** Commit the bindings (XSDs stay untracked).
- **(C)** Hand-written models.

Option (A) has a serious drawback: without locally obtained (proprietary) XSDs, the
schema-dependent part of `EBICO.Core` is **not buildable** — CI (which has no
schemas) could neither compile nor test the bindings and everything built on top.
That would permanently exclude the core of the project from CI coverage.

## Decision

**Option (B): the generated bindings are committed; the XSDs stay untracked
(`.gitignore`, ADR-0003).**

- The source of truth for the build is the **committed `.cs`** files under
  `src/EBICO.Core/Schema/`. CI and contributors build/test without schemas.
- Generation is **reproducible**: `dotnet-xscgen` pinned exactly in
  `.config/dotnet-tools.json`, driven by `scripts/generate-bindings.sh`. It is a
  **maintainer step** (after a schema update), not a build step.
- Details on tool, namespaces and layout: [../protocol/xsd-bindings.md](../protocol/xsd-bindings.md).
- **License:** the written approval of the EBICS SC is pursued **in parallel**
  (`info@ebics.de`); M1 is not blocked on it. Only generated artefacts are
  committed, not the original XSD text; the provenance is documented.

## Consequences

- **CI covers the protocol core for real** (build + round-trip tests run without
  schemas). Bindings are reviewable in the diff.
- On schema updates the bindings must be regenerated and committed along with them;
  a non-deterministic generator change would create noise — hence the exact version
  pin.
- **Residual risk (license):** should the EBICS SC object, the bindings can be
  removed/regenerated — the XSDs were never committed. This is **not legal advice**
  (cf. `ebics-licensing.md`); the final responsibility lies with the operator.

## Alternatives

- **(A) Generate at build time, do not commit** — rejected: makes the core not
  buildable/testable in CI.
- **(C) Hand-written models** — rejected: high effort and error risk given the large
  EBICS schema surface; no direct benefit over generated, reviewed bindings.
