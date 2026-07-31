# 0026 — Conformance against real clients (vendor captures, test tiers, deviation policy)

- Status: accepted
- Date: 2026-07-20

## Context

Milestone M8 requires, in issue **#59**, proof of conformance **against real, foreign
EBICS clients** — not just against our own counterpart (#57/#58). Two hard constraints
collide here:

1. **CI runs offline** and cannot execute a Java/Node/Python foreign client.
2. The official EBICS **XSDs and sample XML are proprietary** (EBICS SC) and are not
   committed (`.gitignore`: `schemas/**/*.xsd`, `tests/**/Fixtures/Xml/**/*.xml`, see
   [ADR-0003](0003-handling-proprietary-schemas.md)).

A pure "skip-if-missing" solution (as with the proprietary samples) would never run in CI
and would never deliver the core of #59 — the proof against *real* foreign bytes.

## Decision

A new test tier `tests/EBICO.Tests/Conformance/` with several layers, plus a clear policy
for captures and for handling found deviations.

**1. Committed vendor captures as the load-bearing layer.** The *output* of a permissively
licensed OSS client (MIT/Apache) is **neither the property of the EBICS SC nor a
derivative of a proprietary XSD/sample file** — it is data generated with throwaway keys.
Such captures **may be committed**, under the path **not** `.gitignore`-d
`tests/EBICO.Tests/Conformance/Vendor/<client>/<version>/request/*.xml` (+ `PROVENANCE.md`).
They thus run **permanently in CI**. Concretely captured: `ebics-client`
(node-ebics-client, MIT, H004), generated with the one-off, local tool
`tools/vendor-capture/` (not part of build/CI). Official ebics.org samples stay separate
from this and remain skip-if-missing.

**2. Wire-shape tolerance deliberately as a parser proxy.** Additional tier-A tests reshape
EBICO's *own* request XML into legitimate foreign forms (namespace prefix instead of the
default incl. `xsi:type` rewriting, whitespace, comments). They are CI-green and check real
parser robustness, but are **not** proof against a foreign emitter — this honesty boundary
is documented.

**3. Document deviations instead of fixing the protocol.** #59 **finds and documents**
deviations; it does **not change** the protocol/binding behaviour. Changes to the generated
bindings or to crypto/serialisation details require the official XSDs/annexes (proprietary,
not in the repo) and follow the principle "Evidence > assumptions". Found deviations are
characterised (tests that capture the *current* behaviour) and described, along with
follow-up work, in
[docs/development/conformance-real-clients.md](../development/conformance-real-clients.md).

## Consequences

- The vendor replay immediately finds the **most important interop deviation** that the
  EBICO↔EBICO tests cannot see by design: EBICO's generated H004 binding types
  `OrderDetails` **abstractly** and demands an `xsi:type` discriminator that EBICO's own
  connector emits but a real client (node-ebics-client) omits. Consequence: **all**
  onboarding requests of the real client are rejected (then as `061099`, i.e. wrongly as a
  server rather than a client error). This was captured as a characterisation test and named
  as follow-up work (type the binding concretely; and map non-deserialisable client XML to a
  client error code) — **done in
  [ADR-0029](0029-interop-fixes-real-clients.md) / issue #117**, together with two further
  defects that only became visible behind it (`A006` on H004, modulus with an ASN.1 sign
  byte).
- The corpus is **extensible**: further clients/versions are added via a directory +
  `PROVENANCE.md` + replay; if the corpus is missing, the replays skip and CI stays green.
- The docs guard `ConformanceMatrixTests` keeps the compatibility-matrix page with its
  mandatory sections in sync (pattern like `OrderCoverageMatrixTests`).
- The M8 epic ([#56](../ticket-overview.md)) is completed with #59; the three sub-issues
  (#57/#58/#59) are done.

## Alternatives

- **Only skip-if-missing (store proprietary samples locally):** rejected — never runs in
  CI, does not fulfil the core of #59 (real foreign bytes).
- **Run a foreign client live in CI (Node/Java):** rejected — CI is offline; a cross-runtime
  handshake in the build is fragile and network-dependent. Capture generation stays one-off
  and local, CI only *replays*.
- **Fix the found `OrderDetails`/`xsi:type` deviation in the binding right away:** rejected
  for #59 — affects generated bindings and EBICO's own wire format (broad blast radius) and
  is not verifiable without the official XSDs. Belongs documented as its own,
  spec-supported follow-up work. → Caught up in
  **[ADR-0029](0029-interop-fixes-real-clients.md)** (issue #117): binding concrete,
  misclassification fixed, `A006` on H004, modulus normalisation — the vendor replay thereby
  turned from a characterisation into a conformance test.
- **Only reshape EBICO's own XML (no vendor captures):** rejected — proves only parser
  tolerance, not conformance against a foreign emitter.
