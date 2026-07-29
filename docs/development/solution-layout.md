# Solution layout & build conventions

This page describes how the EBICO solution is structured and which
project-wide build conventions apply. It belongs to issue **#6 — Create solution &
project scaffold** (Milestone M0).

## Projects

The solution `EBICO.sln` contains the production projects under `src/`, the
test project under `tests/` and — since #50 — a sample under `samples/`:

| Project | SDK | Purpose |
| --- | --- | --- |
| `src/EBICO.Core` | `Microsoft.NET.Sdk` (classlib) | Shared primitives: schemas/serialization, crypto, BTF/order models |
| `src/EBICO.Connector` | `Microsoft.NET.Sdk` (classlib) | NuGet client (mediator pattern) |
| `src/EBICO.Server` | `Microsoft.NET.Sdk.Web` | The EBICS emulator (ASP.NET Core, hostable) |
| `src/EBICO.Suite` | `Microsoft.NET.Sdk.Web` | Blazor Web App (Interactive Server) — admin/inspector UI |
| `tests/EBICO.Tests` | `Microsoft.NET.Sdk` | xUnit v3 test project |
| `samples/EBICO.Connector.Quickstart` | `Microsoft.NET.Sdk` (Exe, `IsPackable=false`) | Runnable connector quickstart (hosts the server in-process) |

### Reference graph

```
EBICO.Core  ◄── EBICO.Connector
     ▲      ◄── EBICO.Server
     └────── ◄── EBICO.Suite

EBICO.Connector.Quickstart ──► EBICO.Connector, EBICO.Server, EBICO.Core
EBICO.Tests ──► EBICO.Core, EBICO.Connector, EBICO.Server, EBICO.Suite, EBICO.Connector.Quickstart
```

`EBICO.Suite → EBICO.Server` has been used since M7 (the UI binds the server state
in-process, ADR-0009). The quickstart sample (#50) references connector, server
and core, so that `dotnet run` shows the full round-trip without an external server; the tests
reference it for the smoke test.

## Build conventions

### `Directory.Build.props` (project-wide)

- `Nullable enable`, `ImplicitUsings enable`, `LangVersion latest`
- `TreatWarningsAsErrors=true` — implementation of the DoD "no new warnings"
- `AnalysisLevel=latest` with the .NET analyzers enabled
- `EnforceCodeStyleInBuild=false` — style rules (`IDExxxx`) from the
  `.editorconfig` only guide the IDE and do **not** break the build; real
  compiler/analyzer warnings are hard via `TreatWarningsAsErrors`
- `GenerateDocumentationFile=true` **only** for `EBICO.Core` and
  `EBICO.Connector` (libraries with a public API). Missing XML doc on
  public members becomes a build error there (CS1591) — a direct implementation
  of the DoD "XML doc on public APIs". Server/Suite are apps without
  published API surface, tests need no doc files.

### Central package management — `Directory.Packages.props`

`ManagePackageVersionsCentrally=true`. **Package versions live exclusively in
`Directory.Packages.props`** (`<PackageVersion …>`); in the `.csproj` the
packages are referenced without a version attribute (`<PackageReference Include="…" />`).
So there is exactly one place per version and no version drift between
projects. `CentralPackageTransitivePinningEnabled=true` additionally pins also
transitive packages — this yields reproducible restores **without**
`packages.lock.json`.

> **Deliberately no lock files (`RestorePackagesWithLockFile`):** The implicit
> Blazor asset package `Microsoft.AspNetCore.App.Internal.Assets` is bound to the
> ASP.NET runtime version of the installed SDK. Checked-in lock files therefore
> break `dotnet restore --locked-mode` between machines with a
> different SDK patch (NU1004). Reproducibility here comes from the
> exact version pinning of central package management; the SDK itself is pinned via
> `global.json`.

### `global.json`

Pins the .NET SDK version to **`10.0.100`** with `rollForward: latestFeature`:
required is .NET 10 (the target framework of all projects), accepted is any
installed 10.0.x SDK from the first feature band onward — the highest of these is
taken. This way local machines and CI build with the same major toolchain, without
a contributor having to install a specific feature band.

> **Why not pin to a higher band (#124):** `latestFeature` only rolls
> **upward**. A pin to, say, `10.0.300` cannot be resolved with an installed
> SDK 10.0.2xx — then every `dotnet` command in the repo fails
> with "A compatible .NET SDK was not found", while CI stays inconspicuously green
> (`actions/setup-dotnet` simply downloads the pinned version).
> The pin therefore names the **lowest** suitable version, not the newest.

### `.editorconfig`

.NET standard conventions: file-scoped namespaces, `var` preferences, 4 spaces
(C#) or 2 spaces (project/config files), `_camelCase` for private fields.

## First primitives

`EBICO.Core` already contains `EbicsVersion` (`H003`/`H004`/`H005`) — the
central version abstraction, referenced among others by the connector DI registration in
[../connector/architecture.md](../connector/architecture.md)
(`o.Version = EbicsVersion.H005`). A smoke test in `EBICO.Tests` checks that
all three versions are present.

## Verification

```bash
dotnet build EBICO.sln -c Release   # ohne Warnungen/Fehler
dotnet test                         # Smoke-Test grün
```
