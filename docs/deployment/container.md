# Betrieb: Container-Image für EBICO.Server

> Umsetzung von **Issue #61** (Milestone M9 — Packaging & Docs). Diese Seite beschreibt das
> **Docker-Container-Image** für den EBICS-Emulator (`EBICO.Server`), die **Konfiguration über
> Umgebungsvariablen** und ein **Beispiel-`docker-compose`** (Server + Suite). Die Images sind für
> lokalen/Emulator-Betrieb gedacht — nicht für den ungeschützten Betrieb in einem
> nicht vertrauenswürdigen Netz (siehe [Sicherheit](#sicherheit)).

## Zweck

`EBICO.Server` ist der EBICS-Server-Emulator (konzeptionell wie *Azurite* für Azure Storage). #61
macht ihn als schlankes Container-Image lauffähig, sodass er ohne lokales .NET-SDK gestartet werden
kann. Ein einziges, parametrisiertes `Dockerfile` im Repo-Root baut wahlweise den Server oder die
Blazor-Suite; ein `docker-compose.yml` startet beide nebeneinander.

## Images & Build

Das `Dockerfile` ist ein **Multi-Stage-Build**:

- **Build-Stage** `mcr.microsoft.com/dotnet/sdk:10.0` — restauriert und published das gewählte
  Projekt (`dotnet publish -c Release`, framework-abhängig).
- **Runtime-Stage** `mcr.microsoft.com/dotnet/aspnet:10.0` — enthält nur das Publish-Ergebnis, läuft
  als **nicht-root** (`USER $APP_UID`) und lauscht auf Port **8080**.

Der Build-Arg **`PROJECT`** wählt das Projekt (Default `EBICO.Server`):

```bash
# Server-Image (Standard):
docker build -t ebico-server:local .

# Suite-Image:
docker build --build-arg PROJECT=EBICO.Suite -t ebico-suite:local .
```

`ENTRYPOINT` ist `["dotnet"]`, `CMD` ist `["EBICO.Server.dll"]`. Für die Suite wird das Kommando auf
`EBICO.Suite.dll` überschrieben (siehe `docker-compose.yml`).

> **SDK-Pin:** `global.json` pinnt das SDK auf `10.0.300` (`rollForward: latestFeature`). Das
> Floating-Tag `sdk:10.0` liefert stets das neueste 10.0.x-SDK (≥ 10.0.300) und erfüllt den Pin. Es
> gibt bewusst **keine** `packages.lock.json` (zentrale Paketverwaltung), daher wird `dotnet restore`
> **nicht** im `--locked-mode` ausgeführt.

Der Container starten & prüfen:

```bash
docker run --rm -p 5014:8080 ebico-server:local
# Liveness:
curl -i http://localhost:5014/health        # -> 200 "Healthy"
```

## Konfiguration via ENV

Zwei Ebenen greifen im Container über Umgebungsvariablen:

**1. Standard-ASP.NET-Core-Host-Variablen** (vom Framework verarbeitet):

| Variable | Zweck | Beispiel |
| --- | --- | --- |
| `ASPNETCORE_HTTP_PORTS` | HTTP-Port(s) von Kestrel | `8080` (Image-Default) |
| `ASPNETCORE_URLS` | vollständige Bind-URLs (überschreibt `*_PORTS`) | `http://+:8080` |
| `ASPNETCORE_ENVIRONMENT` | Umgebung | `Production` |
| `Logging__LogLevel__Default` | Log-Level | `Information` |
| `AllowedHosts` | erlaubte Hosts | `*` |

**2. Emulator-Optionen** (`EbicoServerOptions`) — gebunden aus der Konfigurations-Section
**`Ebico`**. Im Container werden verschachtelte Keys per **Doppel-Unterstrich** gesetzt
(`Ebico__<Property>`). Beispiele:

| Umgebungsvariable | Wirkung | Default |
| --- | --- | --- |
| `Ebico__EndpointPath` | Pfad des EBICS-Endpoints | `/ebics` |
| `Ebico__AdminApiPath` | Prefix der Admin-API | `/admin` |
| `Ebico__FallbackResponseVersion` | Fehler-Antwortversion bei unerkannter Version | `H005` |
| `Ebico__MaxRequestBodyBytes` | max. Request-Body (Bytes) | `1048576` |
| `Ebico__SegmentSizeBytes` | Roh-Segmentgröße (Bytes) | `524288` |
| `Ebico__TransactionTimeout` | Idle-Timeout je Transaktion (`hh:mm:ss`) | `01:00:00` |
| `Ebico__MaxConcurrentTransactions` | Obergrenze paralleler Transaktionen (`0` = unbegrenzt) | `0` |
| `Ebico__MaxEventLogEntries` | Ring-Puffergröße des Ereignis-Logs | `10000` |

Alle Felder von `EbicoServerOptions` sind so überschreibbar (siehe
[`src/EBICO.Server/EbicoServerOptions.cs`](../../src/EBICO.Server/EbicoServerOptions.cs)).

```bash
docker run --rm -e Ebico__EndpointPath=/custom-ebics -p 5014:8080 ebico-server:local
curl -sk -X POST http://localhost:5014/custom-ebics -H "Content-Type: text/xml" --data "<x/>"
```

**Precedence** (spätere Quelle gewinnt je Property): Defaults < `Ebico`-Config/ENV <
Code-`configure`-Delegate an `AddEbicoServer(...)`. Die Bindung ist **null-sicher**: fehlt eine
`IConfiguration` (z. B. in Unit-Tests mit einer nackten `ServiceCollection`), bleiben die Defaults.

## docker-compose (Server + Suite)

`docker-compose.yml` im Repo-Root startet beide Hosts:

```bash
docker compose up --build
#   server -> http://localhost:5014
#   suite  -> http://localhost:5267
```

Beide Services werden aus demselben `Dockerfile` gebaut (via `PROJECT`-Build-Arg); der `suite`-Service
überschreibt das Startkommando mit `EBICO.Suite.dll`.

> **Kein geteilter Live-Zustand:** Suite und Server teilen heute **keinen** Zustand. Die Suite betreibt
> einen eigenen In-Memory-Store mit geseedeten Beispieldaten und spricht den Server **nicht** über HTTP
> an ([ADR-0009](../adr/0009-blazor-render-mode.md)); die prozessübergreifende Live-Inspektion gegen
> einen laufenden Server ist ein dokumentiertes Folgethema ([ADR-0015](../adr/0015-ereignis-protokollspeicher.md)).
> Das compose zeigt also „beide laufen", nicht „gekoppelt".

Die Suite ruft `UseHttpsRedirection()` auf; ohne konfigurierten HTTPS-Port loggt sie beim Start eine
Warnung und liefert die Inhalte weiter über HTTP aus (im Container unkritisch; TLS terminiert man
üblicherweise an einem vorgelagerten Proxy).

## Sicherheit

Der EBICS-Endpoint ist unsigniert, und die **Admin-API (`/admin`) ist unauthentifiziert by design** —
der Server ist ein lokaler Emulator (wie *Azurite*). Das Container-Image ändert daran nichts:

- **Nicht** ungeschützt in ein nicht vertrauenswürdiges Netz exponieren.
- Bevorzugt an `127.0.0.1` binden bzw. hinter einem authentifizierenden Reverse-Proxy betreiben.
- Kein Secrets-Management im Image; Konfiguration erfolgt über ENV/Config.

## Health

Der Server mappt einen Liveness-Endpoint **`/health`** (`AddHealthChecks()` /
`MapHealthChecks("/health")`, Antwort `200 "Healthy"`). Er dient Orchestrator-Probes
(Kubernetes-Liveness/-Readiness) und externen Checks. Ein `healthcheck` im `docker-compose.yml`
entfällt bewusst, weil das `aspnet`-Runtime-Image keinen HTTP-Client (`curl`/`wget`) mitbringt —
die Probe erfolgt vom Host bzw. vom Orchestrator.

## CI

Die CI (`.github/workflows/ci.yml`) baut das Server-Image in einem eigenen Job `container-build`
(**build-only**, kein Registry-Push), damit das `Dockerfile` nicht verrottet. Der Push in eine
Registry ist Bestandteil der NuGet-/Publish-Pipeline (#62).

## Tests

- [`tests/EBICO.Tests/Docs/ContainerArtifactsTests.cs`](../../tests/EBICO.Tests/Docs/ContainerArtifactsTests.cs) —
  Guard-Tests: `Dockerfile`, `.dockerignore`, `docker-compose.yml` und diese Doku existieren, sind im
  Doku-Index verlinkt und enthalten die erwarteten Kern-Bestandteile (Base-Images, `PROJECT`-Arg,
  Service-Namen, ADR-Verweis).
- [`tests/EBICO.Tests/Server/EbicoServerOptionsConfigurationTests.cs`](../../tests/EBICO.Tests/Server/EbicoServerOptionsConfigurationTests.cs) —
  Bindung von `EbicoServerOptions` aus der `Ebico`-Config-Section (Happy Path), Precedence des
  Code-Delegates und Null-Sicherheit ohne `IConfiguration`.
- [`tests/EBICO.Tests/Server/HealthEndpointIntegrationTests.cs`](../../tests/EBICO.Tests/Server/HealthEndpointIntegrationTests.cs) —
  End-to-End über `WebApplicationFactory`: `/health` liefert 200; ein per Konfiguration gesetzter
  `Ebico__EndpointPath` steuert nachweislich den gemappten EBICS-Pfad.

## Verwandte Doku

- [Hostable Server-Grundgerüst](../server/host.md) — `Program.cs`, `AddEbicoServer`, `EbicoServerOptions`, Pipeline
- [CI-Pipeline (GitHub Actions)](../development/ci.md) — Build/Test, Container-Build-Job
- [ADR-0022 — Container-Image & ENV-Konfiguration](../adr/0022-container-image-und-konfiguration.md)
- [ADR-0009 — Blazor Render-Modus](../adr/0009-blazor-render-mode.md)
