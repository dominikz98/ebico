# 0022 — Container image & ENV configuration

- Status: accepted
- Date: 2026-07-16

## Context

M9 ([#61](../deployment/container.md)) requires making the EBICS emulator
(`EBICO.Server`) runnable as a Docker container: a **Dockerfile**, **configuration via
ENV** and a **sample `docker-compose`** (server + Suite). Starting point: no Docker
infrastructure at all; server and Suite are two independent
`Microsoft.NET.Sdk.Web`/net10.0 hosts on separate ports. The server needs no HTTPS to
start and no NuGet package other than the Core reference.

Two decisions were needed: (a) **how** the image is built (base images, one or several
Dockerfiles, start command for two projects) and (b) **how** the emulator is configured
in the container — because `EbicoServerOptions` had until then **not** been bound from
configuration (only defaults + an optional code delegate to `AddEbicoServer`), so ENV
overrides of the emulator options did not take effect at all.

## Decision

1. **Multi-stage build, official .NET images.** Build stage
   `mcr.microsoft.com/dotnet/sdk:10.0`, runtime stage
   `mcr.microsoft.com/dotnet/aspnet:10.0`. The floating tag `10.0` satisfies the
   `global.json` pin (then `10.0.300`, since #124 `10.0.100` — each `latestFeature`; the
   floating tag delivers the newest 10.0.x anyway). No `--locked-mode` (no lockfiles,
   central package management). The runtime runs as **non-root** (`USER $APP_UID`), port
   **8080**.
2. **A single, parameterised Dockerfile at the repo root** with a build arg **`PROJECT`**
   (default `EBICO.Server`). `docker build .` builds the server (the headline artefact);
   `--build-arg PROJECT=EBICO.Suite` builds the Suite. `ENTRYPOINT ["dotnet"]` + `CMD
   ["EBICO.Server.dll"]` — the `suite` service in the compose only overrides the command
   (`EBICO.Suite.dll`). Rationale: both projects are set up almost identically; a second,
   duplicated Dockerfile brings no benefit.
3. **ENV configuration over two levels.** (a) Standard ASP.NET host variables already take
   effect via `WebApplication.CreateBuilder(args)`
   (`ASPNETCORE_HTTP_PORTS`/`ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT`, `Logging__*`).
   (b) `EbicoServerOptions` is newly bound from the config section **`Ebico`**
   (`Ebico__EndpointPath`, `Ebico__MaxRequestBodyBytes`, …). The binding happens in
   `AddEbicoServer` **null-safely** via a dedicated
   `IConfigureOptions<EbicoServerOptions>` registration that resolves `IConfiguration` via
   `GetService` (not `GetRequiredService`) — so unit tests that build a bare
   `ServiceCollection` without `IConfiguration` stay runnable unchanged. Registered
   **before** the optional `configure` delegate, so explicit code overrides the
   configuration (defaults < ENV/config < code).
4. **`docker-compose.yml` starts server + Suite as two independent containers** without
   HTTP coupling and without shared state (see consequences). Additionally: a liveness
   endpoint **`/health`** on the server for orchestrator probes; a CI job
   **`container-build`** (build-only, no push) keeps the Dockerfile green.

## Consequences

- The server is startable without a local .NET SDK; the emulator options are fully
  overridable via ENV. Existing options overrides in integration tests
  (`ConfigureTestServices(...Configure...)`) run last and still win.
- **Suite and server share no live state:** the Suite has its own in-memory store with
  sample data and does not talk to the server over HTTP
  ([ADR-0009](0009-blazor-render-mode.md)). The compose shows "both run", not "coupled";
  cross-process live inspection remains follow-up work
  ([ADR-0015](0015-event-log-store.md)).
- **Security stays at emulator level:** unsigned EBICS endpoint, unauthenticated admin
  API. The image must not be exposed unprotected on an untrusted network (documented in
  [container.md](../deployment/container.md)).
- No registry push at this stage (belongs to the publish pipeline #62); the compose
  contains no `healthcheck` because the `aspnet` image ships no HTTP client.

## Alternatives

- **Separate Dockerfiles per project** (`src/EBICO.Server/Dockerfile`,
  `src/EBICO.Suite/Dockerfile`, the VS convention). Rejected: almost identical content,
  double maintenance; the `PROJECT` build arg covers both from one source.
- **`optionsBuilder.BindConfiguration("Ebico")`** instead of the manual null-safe
  registration. Rejected: it resolves `IConfiguration` internally via
  `GetRequiredService` and would break unit tests with a bare `ServiceCollection`
  (without `IConfiguration`) when resolving `IOptions<EbicoServerOptions>`.
- **Configuration only via the ASP.NET host variables** (no `Ebico` binding). Rejected:
  the emulator options (endpoint path, limits, timeouts) would not be overridable in the
  container at all — "configuration via ENV" would only be half fulfilled.
- **Self-contained/trimmed or AOT publish** for an even smaller image. Rejected (for
  now): extra weight in build/debug without a clear benefit for a local emulator; the
  framework-dependent `aspnet` image suffices.
