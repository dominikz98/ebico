# Erste Schritte — In 5 Minuten zum laufenden Emulator

Der schnellste Weg zu einem laufenden EBICS-Emulator und einem ersten End-to-End-Rundlauf mit dem
Client. Umsetzung von **Issue #63** (Milestone M9 — Packaging & Docs). Voraussetzung: entweder
**Docker** oder das **.NET SDK** gemäß [`global.json`](../global.json) — mehr braucht es nicht (die
generierten Schema-Bindings sind committet, siehe [Schemas & Lizenz](#schemas--lizenz)).

## 1. Emulator starten

Zwei Wege — nimm einen.

### Variante A: Docker (kein .NET SDK nötig)

```bash
docker compose up --build
#   server -> http://localhost:5014
#   suite  -> http://localhost:5267   (Blazor-Admin-/Inspektor-UI)
```

Alternativ nur das veröffentlichte Server-Image (verfügbar nach dem ersten getaggten Release, #62):

```bash
docker run --rm -p 5014:8080 ghcr.io/dominikz98/ebico-server:latest
```

Details & ENV-Konfiguration: [Container-Image](deployment/container.md).

### Variante B: `dotnet run` (aus dem Quellcode)

```bash
dotnet run --project src/EBICO.Server      # lauscht auf http://localhost:5014
```

### Läuft er? (Liveness prüfen)

```bash
curl -i http://localhost:5014/health       # -> 200 "Healthy"
```

Der EBICS-Endpoint liegt unter **`/ebics`**, die (unauthentifizierte) Admin-API unter **`/admin`** — der
Server ist ein lokaler Emulator (wie *Azurite*), **nicht** für ungeschützte Netze gedacht (siehe
[Sicherheit](deployment/container.md#sicherheit)).

## 2. Client ausprobieren (Quickstart-Sample)

Der `EBICO.Connector` lässt sich sofort erleben — das mitgelieferte Sample startet **selbst** einen
Server in-process und fährt den vollen Rundlauf (Schlüssel → Onboarding INI/HIA/HPB → Upload CCT →
Download C53). Es braucht **keinen** separat gestarteten Server und keine echte Bank:

```bash
dotnet run --project samples/EBICO.Connector.Quickstart
```

Erwartete Ausgabe (Ports/IDs variieren):

```text
EBICO.Server läuft auf http://127.0.0.1:52341 (EBICS-Endpoint http://127.0.0.1:52341/ebics, Version H005).
Teilnehmerschlüssel erzeugt (A00x/X002/E002).
Onboarding: INI 000000, HIA 000000, HPB 000000.
Upload (CCT): 000000, TxId ..., 1 Segment(e).
Download (C53): 011000, 1 Segment(e), ... Byte, Einträge: ....
Quickstart erfolgreich abgeschlossen.
```

Wie man denselben Client stattdessen gegen einen **separat laufenden** Server (aus Schritt 1) oder eine
echte Bank richtet, zeigt das DI-Setup (`AddEbicoConnector`, `o.Url = …`) im
[Client-Kern](connector/client-core.md); der Sample-Code liegt in
[`samples/EBICO.Connector.Quickstart`](../samples/EBICO.Connector.Quickstart/README.md).

## Andere EBICS-Versionen (H003 / H004 / H005)

EBICO unterstützt **H003, H004 und H005**. Der Sample läuft mit allen drei; Default ist H005, umschalten
per Argument oder Umgebungsvariable:

```bash
dotnet run --project samples/EBICO.Connector.Quickstart -- --version H004
EBICO_QUICKSTART_VERSION=H003 dotnet run --project samples/EBICO.Connector.Quickstart
```

Im eigenen Code ist es nur die eine Option `o.Version = EbicsVersion.H004;` beim `AddEbicoConnector`
([Client-Kern](connector/client-core.md)); die Pipeline ist ansonsten versionsagnostisch. Hintergrund
zum Multi-Version-Dispatch: [Versions-Dispatch](protocol/version-dispatch.md).

## Schemas & Lizenz

Für Quickstart und Betrieb brauchst du **keine** offiziellen EBICS-Schemas: die generierten C#-Bindings
sind ins Repo committet ([ADR-0006](adr/0006-generierte-xsd-bindings-committen.md)), also bauen und laufen
Server, Sample und Tests ohne weiteres Setup. Die Skripte
[`scripts/fetch-schemas.sh`](../scripts/fetch-schemas.sh) /
[`scripts/generate-bindings.sh`](../scripts/generate-bindings.sh) sind reines **Maintainer-Werkzeug** zum
Aktualisieren der Bindings.

Der EBICO-**Code** steht unter **MIT** ([`LICENSE`](../LICENSE)). Die EBICS-Schemas/-Spezifikationen sind
**proprietäres Eigentum der EBICS SC** und werden **nicht** ins Repo eingecheckt — beim eigenständigen
Beziehen die Terms of Use von [ebics.org](https://www.ebics.org) beachten. Details:
[Schema-Quellen & Lizenz](protocol/schema-sources.md), [Lizenz & Repo-Policy](legal/ebics-licensing.md).

## Nächste Schritte

- [Client-Kern & Konfiguration](connector/client-core.md) — `AddEbicoConnector`, `IEbicsClient.Send`, Options/DI
- [Onboarding](connector/onboarding.md) · [Upload](connector/upload.md) · [Download](connector/download.md) — die Flows im Detail
- [Container-Image](deployment/container.md) — Betrieb, ENV-Konfiguration, docker-compose
- [Dokumentations-Index](index.md) — der vollständige Überblick
