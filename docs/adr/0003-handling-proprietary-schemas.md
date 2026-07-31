# 0003 — Handling proprietary EBICS schemas

- Status: accepted
- Date: 2026-06-21

## Context

The EBICS schemas (XSDs), specifications and official sample XML are the
**proprietary property of the EBICS SC**. Reproduction with a copyright notice is
permitted, modification/derivative uses are not without approval. The project needs
the schemas to build the bindings (from M1 onwards) but must not publish them
unchecked.

## Decision

- **No XSDs and no official sample XML in the repository.** `.gitignore` excludes
  `schemas/**/*.xsd`, the manifests and `tests/**/Fixtures/Xml/**/*.xml`.
- **Local, reproducible acquisition** via `scripts/fetch-schemas.sh` (manual
  download → unpack/sort/SHA-256 manifest).
- Tests that require official samples **skip themselves** (`Assert.Skip`) when the
  files are missing — the suite stays green in CI.

Full classification: [../legal/ebics-licensing.md](../legal/ebics-licensing.md).

## Consequences

- License-compliant: no proprietary content in the public repo.
- Contributors/CI must obtain the schemas (and possibly samples) locally to
  build/test the schema-dependent parts.
- **Follow-up decision (M1 gate) — decided:** whether generated **bindings** are
  committed — resolved in [ADR-0006](0006-commit-generated-xsd-bindings.md)
  (Option B: commit bindings, XSDs stay untracked).

## Alternatives

- **Commit XSDs/samples:** best DX, but legally risky without approval — rejected
  (until approval is possibly obtained).
