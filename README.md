# EBICO

An **EBICS implementation in C# (.NET 10)** — conceptually like *Azurite*,
but for EBICS instead of Azure Storage: a hostable **server emulator** plus a
**client package**. Supported protocol versions: **H003, H004, H005**.

## Projects

| Project | Purpose |
| --- | --- |
| `EBICO.Core` | Shared primitives (schemas/serialisation, crypto, BTF/order models) |
| `EBICO.Connector` | NuGet client for accessing an EBICS server (mediator pattern) |
| `EBICO.Server` | The emulator (hostable, ASP.NET Core) |
| `EBICO.Suite` | Blazor UI (admin/inspector) for the server |
| `EBICO.Tests` | Unit/integration/conformance tests (xUnit v3) |

## Quickstart

**A running emulator in 5 minutes** — start the emulator and drive a first end-to-end round-trip with the
client: **[docs/getting-started.md](docs/getting-started.md)**. In short:

```bash
docker compose up --build                              # emulator (server :5014 + Suite :5267)
dotnet run --project samples/EBICO.Connector.Quickstart   # full round-trip onboarding->upload->download
```

**Development** (build & tests):

```bash
dotnet build EBICO.sln          # builds all projects (warnings = errors)
dotnet test                     # runs the test suite
```

Prerequisite: Docker **or** the .NET SDK according to [`global.json`](global.json).

## Documentation

All documentation lives under [`docs/`](docs/index.md) (Docs-as-Code). Start here:
**[docs/index.md](docs/index.md)**. Architecture of the client package (mediator pattern,
send pipeline, onboarding, design decisions):
[docs/connector/architecture.md](docs/connector/architecture.md).

## Contributing

Work is **issue-driven**: one branch + one pull request per issue,
docs and tests belong in the same PR (project-wide *Definition of Done*, see the
[doc index](docs/index.md) and the PR template). Details on the build setup:
[docs/development/solution-layout.md](docs/development/solution-layout.md).

## License / note

The **EBICO code** is licensed under **MIT** (see [`LICENSE`](LICENSE)). The libraries
published as NuGet packages, `EBICO.Core` and `EBICO.Connector`, carry the
licence metadata accordingly; details on packaging in
[docs/connector/packaging.md](docs/connector/packaging.md).

**Unaffected by that:** the EBICS schemas and specifications are the **proprietary property
of the EBICS SC** and are **not** checked into this repository. They are obtained locally via
[`scripts/fetch-schemas.sh`](scripts/fetch-schemas.sh); see
[docs/protocol/schema-sources.md](docs/protocol/schema-sources.md).
