# Operations: Container image for EBICO.Server

> Implements **Issue #61** (Milestone M9 — Packaging & Docs). This page describes the
> **Docker container image** for the EBICS emulator (`EBICO.Server`), the **configuration via
> environment variables** and an example **`docker-compose`** (server + suite). The images are intended for
> local/emulator operation — not for unprotected operation in an
> untrusted network (see [Security](#security)).

## Purpose

`EBICO.Server` is the EBICS server emulator (conceptually like *Azurite* for Azure Storage). #61
makes it runnable as a slim container image, so that it can be started without a local .NET SDK.
A single, parameterised `Dockerfile` in the repo root builds either the server or the
Blazor suite; a `docker-compose.yml` starts both side by side.

## Images & build

The `Dockerfile` is a **multi-stage build**:

- **Build stage** `mcr.microsoft.com/dotnet/sdk:10.0` — restores and publishes the chosen
  project (`dotnet publish -c Release`, framework-dependent).
- **Runtime stage** `mcr.microsoft.com/dotnet/aspnet:10.0` — contains only the publish result, runs
  as **non-root** (`USER $APP_UID`) and listens on port **8080**.

The build arg **`PROJECT`** selects the project (default `EBICO.Server`):

```bash
# Server-Image (Standard):
docker build -t ebico-server:local .

# Suite-Image:
docker build --build-arg PROJECT=EBICO.Suite -t ebico-suite:local .
```

`ENTRYPOINT` is `["dotnet"]`, `CMD` is `["EBICO.Server.dll"]`. For the suite the command is overridden to
`EBICO.Suite.dll` (see `docker-compose.yml`).

> **SDK pin:** `global.json` pins the SDK to `10.0.100` (`rollForward: latestFeature`, since #124 the
> lowest usable version instead of a high feature band). The floating tag `sdk:10.0` always delivers
> the newest 10.0.x SDK and satisfies the pin. There is deliberately **no** `packages.lock.json`
> (central package management), so `dotnet restore` is **not** run in `--locked-mode`.

Start & check the container:

```bash
docker run --rm -p 5014:8080 ebico-server:local
# Liveness:
curl -i http://localhost:5014/health        # -> 200 "Healthy"
```

## Configuration via ENV

Two levels take effect in the container via environment variables:

**1. Standard ASP.NET Core host variables** (processed by the framework):

| Variable | Purpose | Example |
| --- | --- | --- |
| `ASPNETCORE_HTTP_PORTS` | HTTP port(s) of Kestrel | `8080` (image default) |
| `ASPNETCORE_URLS` | full bind URLs (overrides `*_PORTS`) | `http://+:8080` |
| `ASPNETCORE_ENVIRONMENT` | environment | `Production` |
| `Logging__LogLevel__Default` | log level | `Information` |
| `AllowedHosts` | allowed hosts | `*` |

**2. Emulator options** (`EbicoServerOptions`) — bound from the configuration section
**`Ebico`**. In the container, nested keys are set via **double underscore**
(`Ebico__<Property>`). Examples:

| Environment variable | Effect | Default |
| --- | --- | --- |
| `Ebico__EndpointPath` | path of the EBICS endpoint | `/ebics` |
| `Ebico__AdminApiPath` | prefix of the admin API | `/admin` |
| `Ebico__FallbackResponseVersion` | error response version for an unrecognised version | `H005` |
| `Ebico__MaxRequestBodyBytes` | max. request body (bytes) | `1048576` |
| `Ebico__SegmentSizeBytes` | raw segment size (bytes) | `524288` |
| `Ebico__TransactionTimeout` | idle timeout per transaction (`hh:mm:ss`) | `01:00:00` |
| `Ebico__MaxConcurrentTransactions` | upper bound on parallel transactions (`0` = unlimited) | `0` |
| `Ebico__MaxEventLogEntries` | ring-buffer size of the event log | `10000` |

All fields of `EbicoServerOptions` can be overridden this way (see
[`src/EBICO.Server/EbicoServerOptions.cs`](../../src/EBICO.Server/EbicoServerOptions.cs)).

```bash
docker run --rm -e Ebico__EndpointPath=/custom-ebics -p 5014:8080 ebico-server:local
curl -sk -X POST http://localhost:5014/custom-ebics -H "Content-Type: text/xml" --data "<x/>"
```

**Precedence** (later source wins per property): defaults < `Ebico` config/ENV <
code `configure` delegate on `AddEbicoServer(...)`. The binding is **null-safe**: if an
`IConfiguration` is missing (e.g. in unit tests with a bare `ServiceCollection`), the defaults remain.

## docker-compose (server + suite)

`docker-compose.yml` in the repo root starts both hosts:

```bash
docker compose up --build
#   server -> http://localhost:5014
#   suite  -> http://localhost:5267
```

Both services are built from the same `Dockerfile` (via the `PROJECT` build arg); the `suite` service
overrides the start command with `EBICO.Suite.dll`.

> **No shared live state:** the suite and server share **no** state today. The suite runs
> its own in-memory store with seeded sample data and does **not** talk to the server over HTTP
> ([ADR-0009](../adr/0009-blazor-render-mode.md)); cross-process live inspection against
> a running server is a documented follow-up topic ([ADR-0015](../adr/0015-ereignis-protokollspeicher.md)).
> So the compose shows "both are running", not "coupled".

The suite calls `UseHttpsRedirection()`; without a configured HTTPS port it logs a
warning at startup and serves the content over HTTP (harmless in the container; TLS is usually
terminated at an upstream proxy).

## Security

The EBICS endpoint is unsigned, and the **admin API (`/admin`) is unauthenticated by design** —
the server is a local emulator (like *Azurite*). The container image does not change that:

- Do **not** expose it unprotected to an untrusted network.
- Preferably bind to `127.0.0.1` or run it behind an authenticating reverse proxy.
- No secrets management in the image; configuration is done via ENV/config.

## Health

The server maps a liveness endpoint **`/health`** (`AddHealthChecks()` /
`MapHealthChecks("/health")`, response `200 "Healthy"`). It serves orchestrator probes
(Kubernetes liveness/readiness) and external checks. A `healthcheck` in the `docker-compose.yml`
is deliberately omitted, because the `aspnet` runtime image ships no HTTP client (`curl`/`wget`) —
the probe is performed from the host or from the orchestrator.

## CI & registry push

CI (`.github/workflows/ci.yml`) builds the server image on every push/PR in a dedicated job
`container-build` (**build-only**, no registry push), so that the `Dockerfile` does not rot.

The **push to GHCR** happens in the tag-triggered **release pipeline**
(`.github/workflows/release.yml`, #62 / [ADR-0027](../adr/0027-nuget-publish-und-release-pipeline.md)):
when a tag `vYEAR.MONTH.N` is pushed, the server image is built and pushed to GHCR as
`ghcr.io/dominikz98/ebico-server:{VERSION}` **and** `:latest` — authenticated via
the automatic `GITHUB_TOKEN` (no external secret). Procedure: [Release runbook](../development/release.md).

```bash
# Pull and start the published image:
docker run --rm -p 5014:8080 ghcr.io/dominikz98/ebico-server:latest
curl -i http://localhost:5014/health        # -> 200 "Healthy"
```

## Tests

- [`tests/EBICO.Tests/Docs/ContainerArtifactsTests.cs`](../../tests/EBICO.Tests/Docs/ContainerArtifactsTests.cs) —
  guard tests: `Dockerfile`, `.dockerignore`, `docker-compose.yml` and this doc exist, are linked in
  the doc index and contain the expected core components (base images, `PROJECT` arg,
  service names, ADR reference).
- [`tests/EBICO.Tests/Server/EbicoServerOptionsConfigurationTests.cs`](../../tests/EBICO.Tests/Server/EbicoServerOptionsConfigurationTests.cs) —
  binding of `EbicoServerOptions` from the `Ebico` config section (happy path), precedence of the
  code delegate and null safety without an `IConfiguration`.
- [`tests/EBICO.Tests/Server/HealthEndpointIntegrationTests.cs`](../../tests/EBICO.Tests/Server/HealthEndpointIntegrationTests.cs) —
  end-to-end via `WebApplicationFactory`: `/health` returns 200; an `Ebico__EndpointPath` set via
  configuration demonstrably steers the mapped EBICS path.

## Related docs

- [Hostable server skeleton](../server/host.md) — `Program.cs`, `AddEbicoServer`, `EbicoServerOptions`, pipeline
- [CI pipeline (GitHub Actions)](../development/ci.md) — build/test, container-build job
- [ADR-0022 — Container image & ENV configuration](../adr/0022-container-image-und-konfiguration.md)
- [ADR-0009 — Blazor render mode](../adr/0009-blazor-render-mode.md)
