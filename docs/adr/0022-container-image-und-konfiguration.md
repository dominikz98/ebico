# 0022 — Container-Image & ENV-Konfiguration

- Status: accepted
- Datum: 2026-07-16

## Kontext

M9 ([#61](../deployment/container.md)) verlangt, den EBICS-Emulator (`EBICO.Server`) als
Docker-Container lauffähig zu machen: **Dockerfile**, **Konfiguration via ENV** und ein
**Beispiel-`docker-compose`** (Server + Suite). Ausgangslage: keinerlei Docker-Infrastruktur; Server
und Suite sind zwei eigenständige `Microsoft.NET.Sdk.Web`/net10.0-Hosts auf getrennten Ports. Der
Server braucht kein HTTPS zum Start und kein NuGet-Paket außer der Core-Referenz.

Zwei Entscheidungen waren nötig: (a) **wie** das Image gebaut wird (Base-Images, ein oder mehrere
Dockerfiles, Startkommando für zwei Projekte) und (b) **wie** der Emulator im Container konfiguriert
wird — denn `EbicoServerOptions` wurde bis dahin **nicht** aus der Konfiguration gebunden (nur
Defaults + optionaler Code-Delegate an `AddEbicoServer`), sodass ENV-Overrides der Emulator-Optionen
gar nicht griffen.

## Entscheidung

1. **Multi-Stage-Build, offizielle .NET-Images.** Build-Stage `mcr.microsoft.com/dotnet/sdk:10.0`,
   Runtime-Stage `mcr.microsoft.com/dotnet/aspnet:10.0`. Das Floating-Tag `10.0` erfüllt den engen
   `global.json`-Pin (`10.0.300`, `latestFeature`). Kein `--locked-mode` (keine Lockfiles, zentrale
   Paketverwaltung). Runtime läuft als **nicht-root** (`USER $APP_UID`), Port **8080**.
2. **Ein einziges, parametrisiertes Dockerfile im Repo-Root** mit Build-Arg **`PROJECT`** (Default
   `EBICO.Server`). `docker build .` baut den Server (das Headline-Artefakt); `--build-arg
   PROJECT=EBICO.Suite` baut die Suite. `ENTRYPOINT ["dotnet"]` + `CMD ["EBICO.Server.dll"]` — der
   `suite`-Service im compose überschreibt nur das Kommando (`EBICO.Suite.dll`). Begründung: beide
   Projekte sind nahezu identisch aufgebaut; ein zweites, dupliziertes Dockerfile bringt keinen
   Mehrwert.
3. **ENV-Konfiguration über zwei Ebenen.** (a) Standard-ASP.NET-Host-Variablen greifen bereits über
   `WebApplication.CreateBuilder(args)` (`ASPNETCORE_HTTP_PORTS`/`ASPNETCORE_URLS`,
   `ASPNETCORE_ENVIRONMENT`, `Logging__*`). (b) `EbicoServerOptions` wird neu aus der
   Config-Section **`Ebico`** gebunden (`Ebico__EndpointPath`, `Ebico__MaxRequestBodyBytes`, …). Die
   Bindung erfolgt in `AddEbicoServer` **null-sicher** über eine eigene
   `IConfigureOptions<EbicoServerOptions>`-Registrierung, die `IConfiguration` per `GetService` (nicht
   `GetRequiredService`) auflöst — so bleiben Unit-Tests, die eine nackte `ServiceCollection` ohne
   `IConfiguration` bauen, unverändert lauffähig. Registriert **vor** dem optionalen `configure`-
   Delegate, damit expliziter Code die Konfiguration überschreibt (Defaults < ENV/Config < Code).
4. **`docker-compose.yml` startet Server + Suite als zwei eigenständige Container** ohne HTTP-Kopplung
   und ohne geteilten Zustand (siehe Konsequenzen). Zusätzlich: ein Liveness-Endpoint **`/health`** am
   Server für Orchestrator-Probes; ein CI-Job **`container-build`** (build-only, kein Push) hält das
   Dockerfile grün.

## Konsequenzen

- Der Server ist ohne lokales .NET-SDK startbar; die Emulator-Optionen sind vollständig per ENV
  überschreibbar. Bestehende Options-Overrides in Integrationstests
  (`ConfigureTestServices(...Configure...)`) laufen zuletzt und gewinnen weiterhin.
- **Suite und Server teilen keinen Live-Zustand:** Die Suite hat einen eigenen In-Memory-Store mit
  Beispieldaten und spricht den Server nicht über HTTP an ([ADR-0009](0009-blazor-render-mode.md)). Das
  compose zeigt „beide laufen", nicht „gekoppelt"; die prozessübergreifende Live-Inspektion bleibt
  Folge-Arbeit ([ADR-0015](0015-ereignis-protokollspeicher.md)).
- **Sicherheit bleibt Emulator-Niveau:** unsignierter EBICS-Endpoint, unauthentifizierte Admin-API. Das
  Image darf nicht ungeschützt in ein nicht vertrauenswürdiges Netz exponiert werden (dokumentiert in
  [container.md](../deployment/container.md)).
- Kein Registry-Push in dieser Stufe (gehört zur Publish-Pipeline #62); das compose enthält keinen
  `healthcheck`, weil das `aspnet`-Image keinen HTTP-Client mitbringt.

## Alternativen

- **Getrennte Dockerfiles je Projekt** (`src/EBICO.Server/Dockerfile`, `src/EBICO.Suite/Dockerfile`,
  VS-Konvention). Verworfen: nahezu identischer Inhalt, doppelte Pflege; der `PROJECT`-Build-Arg deckt
  beide mit einer Quelle ab.
- **`optionsBuilder.BindConfiguration("Ebico")`** statt der manuellen null-sicheren Registrierung.
  Verworfen: löst `IConfiguration` intern per `GetRequiredService` auf und würde Unit-Tests mit einer
  nackten `ServiceCollection` (ohne `IConfiguration`) beim Auflösen von `IOptions<EbicoServerOptions>`
  brechen.
- **Konfiguration nur über die ASP.NET-Host-Variablen** (kein `Ebico`-Binding). Verworfen: die
  Emulator-Optionen (Endpoint-Pfad, Limits, Timeouts) wären im Container gar nicht überschreibbar —
  „Konfiguration via ENV" wäre nur halb erfüllt.
- **Self-contained/Trimmed oder AOT-Publish** für ein noch kleineres Image. Verworfen (vorerst):
  Mehrgewicht bei Build/Debug ohne klaren Nutzen für einen lokalen Emulator; das
  Framework-abhängige `aspnet`-Image genügt.
