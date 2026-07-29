# Connector: NuGet packaging & samples

> Implementation of **issue #50** (milestone M6 — Connector, completion). This page describes how the
> two published libraries **`EBICO.Core`** and **`EBICO.Connector`** are built as NuGet packages:
> the package metadata, the **symbols/SourceLink** integration, the package READMEs, the
> **CalVer** versioning and the runnable **quickstart sample**. The basis is the
> [client core](client-core.md) (#46) and the [Connector architecture](architecture.md). The actual
> **publish/push** to a feed is deliberately deferred to **M9 / #62**; #50 lays the foundation and
> validates the packability in the CI. Fundamental decision:
> [ADR-0024](../adr/0024-nuget-packaging-und-versionierung.md).

## Purpose

`EBICO.Connector` is intended as a NuGet client (just as *Azurite* is the counterpart on the server
side). For it to be shippable, the libraries need complete package metadata, debug symbols and
reproducible versioning. Since the connector uses `EBICO.Core` via `ProjectReference`, Core must
**also** be a package — otherwise the connector package would have an unresolvable dependency.

The central place for the shared fields is [`Directory.Build.props`](../../Directory.Build.props),
conditioned on the two library projects (same pattern as the already-present
`GenerateDocumentationFile` rule). Project-specific fields (`Description`, `PackageTags`, the
package `README.md`) live in the respective `.csproj`.

## Two packages (Core + Connector)

| Package | Content | Dependency |
| --- | --- | --- |
| `EBICO.Core` | Shared primitives (schema/serialization, crypto, BTF/order models, return codes) | — |
| `EBICO.Connector` | Client pipeline (mediator pattern), onboarding/upload/download API | `EBICO.Core` (same version) + `Microsoft.Extensions.*` |

On `dotnet pack` of the connector, `EBICO.Core` is **not** embedded but written into the `.nuspec`
as a package dependency (with exactly the same version). A consumer therefore pulls both packages.
The lean third-party dependency list (only `Microsoft.Extensions.*` and — for the INI/HIA letter —
QuestPDF) is thereby preserved.

## Package metadata

Set in [`Directory.Build.props`](../../Directory.Build.props) (shared) or in the `.csproj`
(project-specific):

| Field | Value |
| --- | --- |
| `PackageId` | project name (`EBICO.Core`, `EBICO.Connector`) |
| `Authors` / `Company` | `Dominik Zettl` / `tecvia` |
| `Description` / `PackageTags` | per project in the `.csproj` |
| `PackageLicenseExpression` | `MIT` (see [`LICENSE`](../../LICENSE)) |
| `PackageProjectUrl` / `RepositoryUrl` | `https://github.com/dominikz98/ebico` |
| `PackageReadmeFile` | `README.md` (per project, packed into the package) |
| `IncludeSymbols` / `SymbolPackageFormat` | `true` / `snupkg` |

The XML docs (`GenerateDocumentationFile`, already active for Core+Connector) automatically end up
as `lib/net10.0/<Assembly>.xml` in the package.

## Versioning (CalVer)

The version follows the scheme **`{YEAR}.{MONTH}.{BUILD}`** (calendar versioning, deliberately
**instead of** SemVer — see [ADR-0024](../adr/0024-nuget-packaging-und-versionierung.md)):

```
VersionPrefix = <UTC-Jahr>.<UTC-Monat>.$(EbicoBuildNumber)
```

- **BUILD** comes in the CI from `github.run_number` (`-p:EbicoBuildNumber=…`, monotonically
  increasing), locally the default `0` (→ e.g. `2026.7.0`).
- **Normalization:** NuGet/MSBuild treat version components as integers — the leading zero in the
  month drops (`2026.07.1` → **`2026.7.1`**). This is expected and does not change the ordering.
- Via SourceLink, `AssemblyInformationalVersion` additionally carries the commit SHA (`2026.7.1+<sha>`).

CalVer encodes **no** API compatibility; breaking changes are communicated via release
notes/changelog, not via the version number (trade-off in ADR-0024).

## Symbols & SourceLink

`Microsoft.SourceLink.GitHub` (build-only, `PrivateAssets=all`) embeds the repository/commit info;
`IncludeSymbols=true` + `SymbolPackageFormat=snupkg` produces a `.snupkg` with the `.pdb` alongside
each `.nupkg`. Together with `PublishRepositoryUrl`/`EmbedUntrackedSources` this allows step-debugging
right into the sources of the respective commit. `ContinuousIntegrationBuild` is only set in the CI
(`GITHUB_ACTIONS`) (deterministic paths).

## Package README

Each package brings its own `README.md` (`src/EBICO.Core/README.md`,
`src/EBICO.Connector/README.md`), which is rendered on nuget.org as the package description. They link
with **absolute** GitHub URLs (relative repo links would not resolve on nuget.org).

## Quickstart sample

[`samples/EBICO.Connector.Quickstart`](../../samples/EBICO.Connector.Quickstart/README.md) is a
**self-contained console app**: it starts the `EBICO.Server` emulator **in-process** (Kestrel,
ephemeral loopback port), seeds the master data and drives the full round-trip with the connector.
No external server, no real bank:

```bash
dotnet run --project samples/EBICO.Connector.Quickstart
```

The flow (in `QuickstartRunner.RunAsync`, also callable from tests):

1. generate subscriber keys (`ISubscriberKeyGenerator.GenerateAsync`, A00x/X002/E002),
2. onboarding **INI → HIA → HPB** (bank fingerprints checked in-flow),
3. upload **CCT** (`pain.001`, self-generated, non-proprietary sample data in `SamplePain`),
4. download **C53** (`camt.053`) with parse hook (read out ZIP entries).

A *real* deployment points at the bank URL or at a separately started `EBICO.Server` instead of the
in-process server; the DI setup and `IEbicsClient.Send` stay identical.

## Tests

`tests/EBICO.Tests/Packaging/` secures the feature:

- **`PackageMetadataTests`** — checks reflectively for the `EBICO.Core` and `EBICO.Connector`
  assemblies that the `AssemblyInformationalVersion` matches the CalVer pattern and that
  `Description`/`Company`/`Copyright` are set.
- **`QuickstartSampleTests`** — smoke test: runs `QuickstartRunner.RunAsync` and proves the full
  round-trip (INI/HIA/HPB `000000`, CCT `000000`, C53 **`011000`**).

The actual **package contents** (README, XML doc, license expression, Core dependency, `.snupkg`)
are validated by the CI `pack` job — a faulty README wiring would break `dotnet pack` e.g. with
`NU5039`.

## CI / Publish

The [`pack` job](../development/ci.md) builds both packages on every push/PR after `build-test`
(`*.nupkg` + `*.snupkg`) into `./artifacts` and uploads them as an artifact — **build-only, no
registry push** (regression protection, analogous to the `container-build` job).

The authenticated **push to nuget.org** has happened since **M9 / #62** in the tag-triggered
[release pipeline](../development/release.md) (`.github/workflows/release.yml`,
[ADR-0027](../adr/0027-nuget-publish-und-release-pipeline.md)): a tag `vJAHR.MONAT.N` derives the
version, packs Core + Connector with that version and pushes them (incl. `.snupkg` symbols) to
nuget.org (secret `NUGET_API_KEY`, `--skip-duplicate`); additionally a GitHub release with
auto-generated notes is created. A mere merge publishes nothing — the push only fires on the tag.

## Open points

- `Authors`/`Company` are placeholders; for an official release adjust them if needed to the final
  publisher/company designation.

## Related docs

- [Connector architecture](architecture.md) — overall design, send pipeline
- [Client core & configuration](client-core.md) — #46: `AddEbicoConnector`, options/DI
- [Onboarding](onboarding.md) · [Upload](upload.md) · [Download](download.md) — the flows used in the sample
- [CI pipeline](../development/ci.md) — the `pack` job (build-only)
- [Release runbook](../development/release.md) — set tag → nuget.org/GHCR push (#62)
- [ADR-0024 — NuGet packaging & versioning](../adr/0024-nuget-packaging-und-versionierung.md)
- [ADR-0027 — NuGet publish & release pipeline](../adr/0027-nuget-publish-und-release-pipeline.md)
- [License & repo policy](../legal/ebics-licensing.md) — proprietary EBICS schemas (not part of the packages)

---

> This page is the maintained reference. On changes to the packaging, update it here (and in the
> [doc index](../index.md)).
