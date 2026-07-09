# 0009 — Blazor Render-Modus (Interactive Server)

- Status: accepted
- Datum: 2026-07-09

## Kontext

`EBICO.Suite` ist die Admin-/Inspektor-UI für den Emulator (Milestone M7). Als
.NET-10-Blazor-Web-App muss ein Render-Modus festgelegt werden. Blazor bietet:
Static SSR (nur serverseitig gerendert, keine Interaktivität), Interactive Server
(Interaktivität über eine SignalR-Circuit, Logik läuft am Server), Interactive
WebAssembly (Logik im Browser) und Interactive Auto (erster Aufruf per Server,
danach WebAssembly).

Die Suite läuft im selben Prozess wie ihre Datenquelle: Der EBICS-Emulator-Zustand
(Banks/Partner/Subscriber, später Transaktionen und Schlüssel; Server-Store ab M3,
siehe [ADR-Backlog](README.md)) liegt serverseitig. Es ist eine interne
Betriebs-/Diagnose-Oberfläche ohne Anforderung an Offline-Betrieb oder
Massen-Skalierung.

## Entscheidung

**Interactive Server** als globaler Interaktivitätsmodus der Suite; Interaktivität
wird pro Komponente über `@rendermode InteractiveServer` aktiviert, reine
Anzeige-Seiten bleiben Static SSR.

Damit bleibt der bestehende Aufbau (`AddInteractiveServerComponents()` /
`AddInteractiveServerRenderMode()` in `Program.cs`) bestehen und wird als bewusste
Architekturentscheidung festgeschrieben.

## Konsequenzen

- **Ein Host, keine Projektaufteilung:** kein separates WebAssembly-Client-Projekt
  und kein geteiltes DTO-/Contracts-Projekt nötig.
- **Direkter Zugriff auf serverseitigen Zustand** über DI (z. B.
  `IEmulatorStateProvider`) — die Anbindung an den späteren Emulator-Store (M3)
  bleibt ein In-Process-Aufruf statt einer eigenen HTTP-API.
- Trade-off: pro Client eine offene SignalR-Circuit; für ein internes Admin-Tool
  unkritisch. Latenz bei jeder Interaktion (Server-Roundtrip) ist akzeptabel.
- Kein Offline-/Client-Rechen-Szenario möglich — für diese UI nicht relevant.

## Alternativen

- **Interactive Auto (WASM + Server):** erfordert ein zusätzliches Client-Projekt
  und ein geteiltes Contracts-Projekt; State-Zugriff nur über eine HTTP-API.
  Deutlich mehr Aufwand ohne Nutzen für ein internes Admin-Tool — verworfen.
- **Interactive WebAssembly:** wie Auto client-seitig, dazu Erststart-Latenz durch
  das Laden der Runtime; gleicher API-Zwang — verworfen.
- **Static SSR (ohne Interaktivität):** zu wenig für Inspektor-Interaktionen
  (Filtern, Detail-Ansichten, spätere Aktionen) — verworfen.
