# Suite: UI-Grundgerüst & Navigation

> Umsetzung von **Issue #52** (Milestone M7 — Suite). Diese Seite beschreibt das
> Grundgerüst der `EBICO.Suite`: den festgelegten Render-Modus, Layout/Navigation/
> Theming und die Anbindung an den Emulator-Zustand. Der Render-Modus ist in
> [ADR-0009](../adr/0009-blazor-render-mode.md) begründet. Die konkreten
> Datenansichten folgen in #53 (Stammdaten), #54 (Transaktions-Inspektor) und
> #55 (Schlüssel/Zertifikate).

## Zweck

`EBICO.Suite` ist die Admin-/Inspektor-UI des Emulators (eine .NET-10 Blazor Web
App). #52 liefert das *Skelett*: die Navigation über die M7-Bereiche, ein
EBICO-eigenes Theming und die Mechanik, mit der die UI an den serverseitigen
Zustand gebunden wird. Fachliche CRUD-/Inspektor-Funktionen kommen in den
Folge-Issues; hier steht die tragende Struktur.

## Render-Modus

Die Suite läuft im Modus **Interactive Server** ([ADR-0009](../adr/0009-blazor-render-mode.md)).
Interaktivität wird pro Komponente über `@rendermode InteractiveServer` aktiviert;
das Dashboard bleibt Static SSR, die Stammdaten-Seite ist Static SSR mit
interaktiven Inseln ([#53](stammdaten.md)). So bleibt der Zugriff auf den
serverseitigen Zustand ein In-Process-Aufruf über DI — kein separates
WebAssembly-Client- oder Contracts-Projekt nötig.

## Navigation & Layout

Die Navigation (`Components/Layout/NavMenu.razor`) bildet die vier M7-Bereiche ab;
die Blazor-Template-Demoseiten (Counter/Weather) wurden entfernt.

| Eintrag | Route | Inhalt |
| --- | --- | --- |
| Dashboard | `/` | Kennzahlen des Emulator-Zustands (Anzahl Banken/Partner/Teilnehmer) |
| Stammdaten | `/stammdaten` | Verwaltung der Banken/Partner/Teilnehmer ([#53](stammdaten.md)) |
| Transaktionen | `/transaktionen` | Transaktions-Inspektor ([#54](transaktions-inspektor.md)) |
| Schlüssel | `/schluessel` | Fingerprints, INI-Brief-Vergleich, Test-CA/Schlüssel-Werkzeuge ([#55](schluessel-ansicht.md)) |

Das `MainLayout` behält die Sidebar-Struktur des Templates (Sidebar + Content),
zeigt in der Top-Row aber den EBICO-Titel statt des Template-„About"-Links.

### Beispieldaten-Banner (#124)

Über dem Seiteninhalt steht in **jeder** Ansicht ein `DemoDataBanner`
(`Components/Layout/DemoDataBanner.razor`, `role="note"`): die Suite arbeitet auf einem **eigenen**
In-Memory-Zustand mit geseedeten Stammdaten und Transaktionen und ist **nicht** mit einem separat
gestarteten `EBICO.Server`-Prozess verbunden.

Diese Trennung ist seit [ADR-0009](../adr/0009-blazor-render-mode.md) gewollt und in
[ADR-0015](../adr/0015-ereignis-protokollspeicher.md) sowie im `docker-compose.yml` dokumentiert — sie
war nur **in der Oberfläche selbst** unsichtbar. Wer `docker compose up` fährt, sieht zwei Dienste
nebeneinander und eine UI, die plausible Transaktionen eines Servers zeigt, der sie nie gesehen hat.
Der Banner schließt genau diese Lücke zwischen Doku und Bildschirm.

### Unbekannte Route (#126)

Eine nicht existierende Adresse liefert **HTTP 404** und rendert `Components/Pages/NotFound.razor` im
`MainLayout` — verdrahtet doppelt: über `NotFoundPage` am `Router` (`Components/Routes.razor`) für die
clientseitige Navigation und über `UseStatusCodePagesWithReExecute("/not-found")` in `Program.cs` für
direkte Aufrufe.

Die Seite war bis #126 der unveränderte Blazor-Template-Rest: englischer Text in einer durchgängig
deutschen Oberfläche, **ohne `<PageTitle>`** (leerer Browser-Tab, während jede andere Seite einen Titel
setzt) und mit `<h3>` statt `<h1>`, wodurch das `<FocusOnNavigate Selector="h1" />` desselben Routers
ins Leere griff — die Seite hatte überhaupt keine `h1`. Jetzt deutsch, mit Titel, `h1` und einem Link
zurück zum Dashboard.

## Theming

Ein zurückhaltendes, EBICO-eigenes Theme statt der Template-Defaults; kein
eigenes Design-System. Die Marken-/Akzentfarben liegen als CSS-Custom-Properties
(Design-Tokens) in `wwwroot/app.css` unter `:root` (`--ebico-primary`,
`--ebico-primary-dark`, `--ebico-accent`, `--ebico-sidebar-*`) und werden von den
Scoped-CSS-Dateien (`MainLayout.razor.css`, `NavMenu.razor.css`) sowie den
Dashboard-Karten wiederverwendet. Bootstrap bleibt als Basis erhalten.

## Anbindung an den Server-Zustand

Der serverseitige Emulator-Store (Schlüssel, Transaktionen, Onboarding-Zustand)
entsteht erst im Server-Layer (M3/M4). Damit das Grundgerüst die Anbindung schon
jetzt end-to-end zeigt, greift die UI über eine **Abstraktion** auf ein
Read-Model der vorhandenen `EBICO.Core.Domain`-Aggregate zu:

| Typ | Rolle |
| --- | --- |
| `IEmulatorStateProvider` | Read-Model-Vertrag der Suite: `GetBanksAsync` / `GetPartnersAsync` / `GetSubscribersAsync` |
| `SampleEmulatorStateProvider` | In-Memory-Platzhalter mit deterministischen Beispieldaten (`Bank`/`Partner`/`Subscriber`) |

```csharp
// Program.cs
builder.Services.AddScoped<IEmulatorStateProvider, SampleEmulatorStateProvider>();
```

Die Methoden sind **async** gehalten, damit ein späteres Backend (In-Process-Store
oder HTTP-API) ohne Änderung an den Aufrufstellen eingehängt werden kann.

> **Update (#53):** Der reale Server-Store (M3, [#30](../server/master-data.md)) ist
> inzwischen angebunden. Die registrierte Implementierung ist jetzt die Live-Brücke
> `EmulatorStateProvider` über den in-process `IEbicsStateStore`/`IMasterDataManager`
> (Suite → Server → Core); `SampleEmulatorStateProvider` dient nur noch als Seed- und
> Schlüssel-Quelle. Dashboard und Schlüssel-Ansicht blieben dabei unverändert. Details:
> [Stammdaten-Verwaltung](stammdaten.md).

## Tests

`tests/EBICO.Tests/Suite/` deckt ab:

- `SampleEmulatorStateProviderTests` — reines xUnit: der Stub liefert die erwarteten
  Banken/Partner/Teilnehmer, Teilnehmer referenzieren nur bekannte Partner, decken
  technische Nutzer und mehrere Lifecycle-Zustände ab (Happy Path + Konsistenz).
- `NavMenuTests` — bUnit: die Navigation rendert genau die vier M7-Links und
  **keine** Counter/Weather-Demolinks mehr.
- `DashboardTests` — bUnit mit einem Fake-`IEmulatorStateProvider`: das Dashboard
  zeigt die Kennzahlen aus dem State-Provider.
- `NotFoundPageTests` — bUnit: deutsche `h1`-Überschrift, keine englischen Template-Reste,
  kein `h3`, Rückweg zum Dashboard (#126).

Für Blazor-Komponententests wurde **bUnit** (`Directory.Packages.props`)
aufgenommen; es wird framework-agnostisch mit xUnit v3 genutzt (`BunitContext`).
