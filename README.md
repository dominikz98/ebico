# EBICO

Eine **EBICS-Implementierung in C# (.NET 10)** — konzeptionell wie *Azurite*,
aber für EBICS statt Azure Storage: ein hostbarer **Server-Emulator** plus ein
**Client-Package**. Unterstützte Protokollversionen: **H003, H004, H005**.

## Projekte

| Projekt | Zweck |
| --- | --- |
| `EBICO.Core` | Geteilte Primitives (Schemas/Serialisierung, Krypto, BTF/Order-Modelle) |
| `EBICO.Connector` | NuGet-Client für den Zugriff auf einen EBICS-Server (Mediator-Muster) |
| `EBICO.Server` | Der Emulator (hostbar, ASP.NET Core) |
| `EBICO.Suite` | Blazor-UI (Admin/Inspektor) für den Server |
| `EBICO.Tests` | Unit-/Integration-/Conformance-Tests (xUnit v3) |

## Schnellstart

**In 5 Minuten zum laufenden Emulator** — Emulator starten und einen ersten End-to-End-Rundlauf mit dem
Client fahren: **[docs/getting-started.md](docs/getting-started.md)**. Kurzform:

```bash
docker compose up --build                              # Emulator (Server :5014 + Suite :5267)
dotnet run --project samples/EBICO.Connector.Quickstart   # voller Rundlauf Onboarding->Upload->Download
```

**Entwicklung** (Build & Tests):

```bash
dotnet build EBICO.sln          # baut alle Projekte (Warnings = Errors)
dotnet test                     # führt die Test-Suite aus
```

Voraussetzung: Docker **oder** das .NET SDK gemäß [`global.json`](global.json).

## Dokumentation

Die gesamte Doku liegt unter [`docs/`](docs/index.md) (Docs-as-Code). Einstieg:
**[docs/index.md](docs/index.md)**. Architektur des Client-Pakets (Mediator-Muster,
Send-Pipeline, Onboarding, Designentscheidungen):
[docs/connector/architecture.md](docs/connector/architecture.md).

## Mitarbeit

Gearbeitet wird **issue-getrieben**: ein Branch + ein Pull Request pro Issue,
Doku und Tests gehören in denselben PR (projektweite *Definition of Done*, siehe
[Doku-Index](docs/index.md) und das PR-Template). Details zum Build-Setup:
[docs/development/solution-layout.md](docs/development/solution-layout.md).

## Lizenz / Hinweis

Der **EBICO-Code** steht unter der **MIT-Lizenz** (siehe [`LICENSE`](LICENSE)). Die als
NuGet veröffentlichten Bibliotheken `EBICO.Core` und `EBICO.Connector` tragen die
Lizenz-Metadaten entsprechend; Details zum Paketieren in
[docs/connector/packaging.md](docs/connector/packaging.md).

Davon **unberührt**: Die EBICS-Schemas und -Spezifikationen sind **proprietäres Eigentum
der EBICS SC** und werden **nicht** in dieses Repository eingecheckt. Sie werden lokal über
[`scripts/fetch-schemas.sh`](scripts/fetch-schemas.sh) bezogen; siehe
[docs/protocol/schema-sources.md](docs/protocol/schema-sources.md).
