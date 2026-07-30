---
name: ebics-feature-workflow
description: >-
  The binding process for every feature or bugfix in EBICO, including the Definition of Done.
  Use as soon as a change is being implemented that ends up in a PR as a feature/bugfix — from
  creating the branch through code, docs and ADR to tests, green CI and the PR. Encapsulates the
  project-wide DoD rules (docs-as-code, tests, no new warnings, XML doc, review) and the repo conventions.
---

# Feature/bugfix workflow (Definition of Done)

Issue-driven, one branch + one PR per issue. Order:

## 1. Branch

- Branch off `main`. Naming scheme: **`feat/<no>-<slug>`** (e.g. `feat/59-conformance-real-clients`).
- Commit/push only when the user asks for it. Never commit directly on `main`.

## 2. Code

- Reuse existing patterns (see skills `ebics-order-handler`, `ebics-connector`, `ebics-suite`,
  `ebics-crypto`). Mind the multi-version dispatch (H003/H004/H005) where relevant.
- `TreatWarningsAsErrors` is active, central package management (`Directory.Packages.props`), `Nullable enable`.

## 3. Docs (docs-as-code, in the **same** PR)

- New page `docs/<area>/<name>.md` (`protocol/`, `server/`, `connector/`, `suite/`, `development/`,
  `deployment/`, `legal/`).
- **Link it in `docs/index.md`** under the matching rubric (otherwise it is a useless doc orphan).
- If the change concerns order types: update `docs/server/order-coverage-matrix.md`.
- Make "spec caveats" explicit wherever something is design intent rather than XSD-verified.
- **English is the project language** (see CLAUDE.md → "Way of working"): docs, ADRs, code comments,
  commit messages and PR descriptions are written in English (British spelling).

## 4. ADR (for design decisions)

- New file `docs/adr/NNNN-<kebab-title>.md` with the **next free number** (the stock currently ends
  at 0031); the slug is **English** (the existing ADRs 0001–0031 still carry German slugs until #134
  renames them). MADR-lite: context / decision / consequences / alternatives, status `accepted`.
- Register it in the ADR index `docs/adr/README.md`.

## 5. Tests

- Every feature: unit tests for the happy path **and** negative/edge cases. Protocol/crypto logic against
  test vectors and sample XML, not just self-consistency.
- The test folders mirror the product folders (`tests/EBICO.Tests/{Core,Server,Connector,Crypto,Suite,E2E,…}`).
- E2E/conformance: see skill `ebics-conformance-test`.

## 6. CI green

- `dotnet build` + `dotnet test` (Release), **no new warnings**.
- `docs-link-check` (lychee offline over `**/*.md`) — avoid dead links.
- Further CI jobs: `container-build` (server image), `pack` (NuGet Core+Connector, CalVer, build-only).
- Tag-triggered release (`release.yml`, #62/ADR-0027): publish to nuget.org + GHCR, auto release notes
  (only on `v*.*.*` tags; runbook `docs/development/release.md`).
- **`main` is protected** (#3/ADR-0028): all four `ci.yml` jobs are required checks (`strict`),
  `enforce_admins` is on. Direct pushes to `main` and merges with red CI are blocked — for admins as
  well. If a CI job is renamed/added, both the list in `docs/development/ci.md`
  (guard test `BranchProtectionDocTests`) **and** the repo setting have to be brought along.

## 7. PR

- Body following `.github/PULL_REQUEST_TEMPLATE.md` (GitHub pre-fills it automatically).
- **Linking an issue is mandatory:** every PR contains **`Closes #<no>`** and references exactly
  one issue — including pure tooling/docs changes.
- Tick the checklist off completely, in particular **"Docs/skills updated?"** (see below).
- Carry out a code review.

## Context/docs maintenance (part of the Definition of Done)

If the PR touches a pattern described in `docs/`, `CLAUDE.md` or a skill, updating that belongs in
**the same** PR (see CLAUDE.md → "Maintaining context, docs & skills").
Skills point at concrete symbols/paths and otherwise go stale silently.

## Meta/tooling changes

Changes to `.claude/`, `CLAUDE.md` or the CI also get **their own issue** + their own small
branch — do not mix them into a functional feature branch.

## Sources

`CLAUDE.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/workflows/ci.yml`, `docs/adr/README.md`,
`docs/development/ci.md`, `docs/development/testing.md`, `docs/index.md`, `docs/ticket-overview.md`.
