# EBICS schemas/specs — license & repo policy

This page classifies the licensing situation of the EBICS schemas/specifications and lays down
the **repo policy** derived from it. It belongs to Issue **#5 —
License/Terms-of-Use clarification** (Milestone M0).

> ⚠️ **Not legal advice.** This is a technical/organisational classification based
> on the publicly viewable EBICS terms of use, not
> legal advice. The binding decision — in particular on the open question
> of the generated bindings (see below) — and, where applicable, contacting the
> EBICS SC rest with the operator of the project.

## Starting point

The EBICS schemas (XSDs) and specifications are **proprietary property of the
EBICS SC** (EBICS Société par Actions Simplifiée). Based on the sources
(see [../protocol/schema-sources.md](../protocol/schema-sources.md)), the following applies:

| | |
| --- | --- |
| ✅ **Permitted** | Download and **reproduction** of the schemas/specs **with the full copyright notice** (non-exclusive, non-sublicensable). |
| ❌ **Not permitted** (without written approval of the EBICS SC) | **Modification** and **derivative uses** of the schemas/specs. |
| ⚠️ **Trademark/designation** | Products that are **not** based on the published specs may not be called "EBICS" / carry the logo. |

## Repo policy (decision)

1. **No XSD files in the repository.** The EBICS XSDs are **not** checked in.
   `.gitignore` excludes them:
   - `schemas/**/*.xsd`, `schemas/**/MANIFEST.sha256`, `schemas/manifest.json`
2. **No official sample XML in the repository.** The EBICS samples
   (ebics.org) are likewise proprietary and are not checked in:
   - `tests/**/Fixtures/Xml/**/*.xml`
3. **Local, reproducible retrieval** via
   [`scripts/fetch-schemas.sh`](../../scripts/fetch-schemas.sh): manual download
   (expiring securedl URLs, "I accept") → script unpacks/sorts/verifies via
   SHA-256 manifest into `schemas/<VERSION>/`.
4. **Copyright notices are preserved.** On local retrieval, the
   original headers of the files are not removed; derived artifacts point to
   the origin.

This policy is already implemented (M0): `.gitignore`, `fetch-schemas.sh`,
`schema-sources.md` and the test-fixture READMEs reflect it.

## Generated bindings: "derivative works"? (M1 gate — decided)

M1 generates **C# bindings** (classes) from the XSDs. Whether these count as a **derivative use**
of the proprietary schemas was the M1 gate. **Decided (Option B,
[../adr/0006-generierte-xsd-bindings-committen.md](../adr/0006-generierte-xsd-bindings-committen.md)):
the bindings are committed, the XSDs themselves stay untracked.** This way CI builds/tests
the protocol core without proprietary schemas; the written approval
of the EBICS SC is pursued in parallel.

Options (for classification, details in [ADR-0006](../adr/0006-generierte-xsd-bindings-committen.md)):

- **(A) Do not commit the bindings — generate them at build time from locally obtained XSDs.**
  Conservative, no generated derivatives in the repo. Drawback: contributor/CI
  need the locally obtained XSDs to build → the schema-dependent part of
  `EBICO.Core` is not buildable/testable without schemas. **Rejected.**
- **(B) Commit the bindings (XSDs stay untracked).** Best developer experience;
  CI builds/tests the protocol core without proprietary schemas. **Chosen.**
- **(C) Hand-written models** instead of generated bindings — no direct
  derivative use of the XSD text, but at significantly higher effort and error risk.

**Decision:** **(B)** — the generated bindings are committed (XSDs
stay untracked), approval of the EBICS SC pursued in parallel. Rationale and
consequences: [ADR-0006](../adr/0006-generierte-xsd-bindings-committen.md). Only
generated artifacts are committed, not the original XSD text; should the
EBICS SC not permit this, the bindings can be removed/regenerated.

## Relation to EBICO

"EBICO" is an independent emulator/client implementation and carries no
EBICS branding. Conformance claims are only permissible insofar as the implementation
matches the published specs (cf. M8 — Validation & Conformance).

## References

- [Schema sources & retrieval](../protocol/schema-sources.md)
- [`scripts/fetch-schemas.sh`](../../scripts/fetch-schemas.sh)
- EBICS Terms of Use: <https://www.ebics.org/en/informationen/disclaimer>
