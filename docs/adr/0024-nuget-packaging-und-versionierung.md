# 0024 — NuGet packaging & versioning (CalVer) of the connector

- Status: accepted
- Date: 2026-07-17

## Context

M6 (#50) requires making `EBICO.Connector` deliverable as a NuGet package: package
metadata, symbols, README, samples and a versioning strategy. Starting point: no
packaging metadata at all, no `LICENSE`, no versioning/tags, no `samples/` folder. The
connector references `EBICO.Core` via a `ProjectReference` — a deliverable connector
package therefore needs Core as a package too.

Several points were to be decided: license, version scheme, package granularity (one vs.
two packages), the structure of the sample and the delimitation from publish (M9 / #62).

## Decision

1. **Two packages:** `EBICO.Core` and `EBICO.Connector` are both packed
   (`IsPackable=true`); the connector depends as a package dependency on the Core package
   of the **same version**. Core is **not** embedded into the connector (no DLL
   bundling/ILRepack). Server/Suite and tests stay non-packable.
2. **License MIT:** the EBICO **code** is under MIT (`PackageLicenseExpression=MIT`,
   `LICENSE` at the repo root). The proprietary EBICS schemas/specs (not part of the
   packages) remain unaffected by this.
3. **Versioning: CalVer `{YEAR}.{MONTH}.{BUILD}`** (deliberately **instead of** SemVer, on
   explicit request). `VersionPrefix` is computed in `Directory.Build.props` from the UTC
   year/month + `EbicoBuildNumber`; BUILD comes from `github.run_number` in CI, locally
   default `0`. NuGet normalises the components to integers (`2026.07.1` → `2026.7.1`).
4. **Symbols + SourceLink:** `IncludeSymbols=true` + `SymbolPackageFormat=snupkg` and
   `Microsoft.SourceLink.GitHub` (build-only) for step-debugging down into the commit
   sources.
5. **Package READMEs:** one `README.md` each in `src/EBICO.Core` and `src/EBICO.Connector`
   (`PackageReadmeFile`), with absolute GitHub links.
6. **Sample:** a standalone quickstart (`samples/EBICO.Connector.Quickstart`) that hosts
   the `EBICO.Server` in-process and runs the full round-trip — `dotnet run` without setup.
7. **CI:** a **build-only** `pack` job (Core+Connector → artefact, no push), analogous to
   `container-build`. **Publish/push** to a feed stays M9 / #62.

Shared metadata lives centrally in `Directory.Build.props` (conditioned on the two
libraries), project-specific fields in the respective `.csproj`. Details:
[../connector/packaging.md](../connector/packaging.md).

## Consequences

- Both packages are reproducibly packable; CI proves this on every run (e.g. `NU5039` on a
  missing README).
- A consumer pulls `EBICO.Connector` **and** transitively `EBICO.Core` in exactly the same
  version.
- **Trade-off CalVer:** the version encodes **no** API compatibility (unlike SemVer).
  Breaking changes must be communicated via release notes/changelog. The month loses its
  leading zero in the normalised version.
- The quickstart deliberately also references `EBICO.Server` (in-process hosting) — it is
  a self-contained demo, not a pure consumer view.
- `Authors`/`Company` are placeholders and may need adjusting for an official release.

## Alternatives

- **SemVer (e.g. via MinVer, tag-driven):** the more usual scheme with API semantics, but
  rejected in favour of the explicitly requested date versioning.
- **One package with embedded Core (ILRepack/DLL bundling):** a single consumer package,
  but rejected — Core is also used by server/Suite and is a standalone public library;
  embedding risks type-identity conflicts. Two packages are the .NET standard way.
- **Consumer-only sample (connector only, external server):** a more realistic consumer
  view, but not "out-of-the-box" runnable; rejected in favour of the self-contained
  quickstart.
- **Pack + publish right away in CI:** rejected — the authenticated push
  (feed/secrets/permissions) belongs to the publish pipeline M9 / #62; #50 delivers only
  build-only pack.
