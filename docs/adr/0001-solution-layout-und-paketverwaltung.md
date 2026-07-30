# 0001 — Solution layout & central package management

- Status: accepted
- Date: 2026-06-21

## Context

EBICO consists of several components (shared primitives, client, server, UI,
tests) targeting .NET 10. It needs a consistent, reproducible build setup with
uniform conventions across all projects.

## Decision

- **Five projects** under the solution folders `src/` and `tests/`: `EBICO.Core`,
  `EBICO.Connector`, `EBICO.Server`, `EBICO.Suite`, `EBICO.Tests`. Reference graph:
  Connector/Server/Suite → Core; Tests → Core/Connector/Server.
- **`Directory.Build.props`** sets `Nullable`, `ImplicitUsings`,
  `TreatWarningsAsErrors=true`, `AnalysisLevel=latest` for all projects.
  `GenerateDocumentationFile` (mandatory XML doc) only for the libraries
  `Core` + `Connector`.
- **Central package management** (`Directory.Packages.props`,
  `ManagePackageVersionsCentrally=true`) with transitive pinning
  (`CentralPackageTransitivePinningEnabled=true`).
- **No `packages.lock.json` / no `--locked-mode`.** The implicit
  Blazor asset package `Microsoft.AspNetCore.App.Internal.Assets` is bound to the
  SDK runtime patch; checked-in lock files break `--locked-mode` between machines
  with a different SDK patch (NU1004). Reproducibility comes from the exact version
  pinning of CPM; the SDK is pinned via `global.json`.

Details: [../development/solution-layout.md](../development/solution-layout.md).

## Consequences

- Uniform compiler/analyzer policy; every new warning breaks the build (DoD).
- Exactly one place per package version, no version drift.
- Reproducible restores without the lock-file maintenance overhead.
- Trade-off: reproducibility hinges on exact CPM versions + SDK pin rather than
  lock files; when the SDK changes, `global.json` must be updated deliberately.

## Alternatives

- **`packages.lock.json` + `--locked-mode`:** stronger guarantee, but impractical
  in CI due to the SDK-bound Blazor assets (NU1004) — rejected.
- **Per-project versions without CPM:** rejected because of the version-drift risk.
