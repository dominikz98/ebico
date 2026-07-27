---
name: ebics-suite
description: >-
  Anleitung zur Arbeit an der EBICO.Suite — der Blazor-Web-App (Interactive Server) als Admin-/Inspektions-
  Oberfläche für den Emulator. Verwenden beim Hinzufügen/Ändern von Seiten oder Komponenten: Stammdaten-
  Verwaltung (Banken/Partner/Teilnehmer), Transaktions-Inspektor, Schlüssel-/Zertifikats-Ansicht. Deckt
  Render-Modus, den in-process-Zugriff auf den Server-Zustand, die Projektionen (Message-Capture/EventLog)
  und die bUnit-Test-Konvention ab.
---

# EBICO.Suite (Blazor Admin-UI)

Blazor Web App, Render-Modus **Interactive Server** (ADR-0009). Die Suite referenziert `EBICO.Server`
und nutzt dessen Dienste **in-process** — kein HTTP gegen den Emulator. Vor Änderungen die passende
Seite unter `docs/suite/` lesen.

## Struktur

- Komponenten: `src/EBICO.Suite/Components/` (`Pages/`, `Stammdaten/`, `Keys/`, Layout).
- Services/Adapter: `src/EBICO.Suite/Services/` (`IEmulatorStateProvider` + `EmulatorStateProvider` /
  `SampleEmulatorStateProvider`, `ITransactionInspectorProvider` + `TransactionInspectorProvider`,
  Seeder für Beispieldaten).
- Statik: `src/EBICO.Suite/wwwroot/`.

## Anbindung an den Emulator-Zustand (in-process)

- **Stammdaten** (`docs/suite/stammdaten.md`): CRUD über `IMasterDataManager`
  (`src/EBICO.Server/State/IMasterDataManager.cs`) — Banken/Partner/Teilnehmer inkl. Status &
  Berechtigungen, referentielle Integrität serverseitig. Beispieldaten via Seeder.
- **Transaktions-Inspektor** (`docs/suite/transaktions-inspektor.md`): zwei Projektionen —
  Roh-XML je Phase aus `IMessageCaptureStore` (ADR-0021) und die globale Protokollansicht aus
  `IEventLog` (alle Kunden, Live-Filter Kunde/Zeitraum/Typ/Severity). In-process (ADR-0015:
  prozessübergreifende Live-Inspektion bleibt Folgethema).
- **Schlüssel-/Zertifikats-Ansicht** (`docs/suite/schluessel-ansicht.md`): Fingerprints anzeigen,
  INI-Brief-Vergleich (`PublicKeyFingerprint.Verify`), Test-CA/Schlüssel-Werkzeuge; PDF via
  QuestPDF (ADR-0010).

> **Die Suite zeigt ihren eigenen Zustand, nicht den eines laufenden Servers.** Sie hostet die
> Server-Stores in-process und seedet sie; ein separat gestarteter `EBICO.Server`-Prozess bleibt
> unsichtbar (ADR-0009/ADR-0015). Der `DemoDataBanner` im `MainLayout` sagt das seit #124 in der
> Oberfläche — bei neuen Ansichten also **keine** Formulierung wählen, die Live-Daten eines fremden
> Servers suggeriert („Transaktionen des Emulators" o. Ä.).

## Interaktive Inseln bleiben nicht von allein konsistent (ADR-0031)

Mehrere interaktive Inseln auf **einer** Seite (so die Stammdaten-Seite: `BankManager` +
`PartnerManager` + `SubscriberManager`) sind eigenständige Komponenten mit **je eigener
Zustandskopie**, schreiben aber durch denselben `IMasterDataManager` — und die Beziehungen kaskadieren.
Ohne Benachrichtigung entwertet jede Mutation die Geschwister still (#126). Deshalb gilt für **jede**
Komponente, die Stammdaten anzeigt:

1. `@implements IDisposable` + `@inject IMasterDataChangeNotifier Changes`; in `OnInitializedAsync`
   `Changes.Subscribe(handler)`, in `Dispose` das Abo zurückgeben.
2. Nach **jeder** erfolgreichen Mutation `await Changes.NotifyChangedAsync()`.
3. Im Handler `InvokeAsync(...)` verwenden — die Benachrichtigung kommt auf dem Thread des
   auslösenden Circuits an (der Notifier ist ein **Singleton**, damit auch Sitzungen konvergieren).
4. Nach dem Neuladen **transiente UI-Zustände prüfen** (offenes Formular mit gelöschter Bank,
   Löschbestätigung für kaskadierten Datensatz, Detailbereich ohne Datensatz). Neuladen allein
   repariert nur die Tabellen.

> Kein Wächter erzwingt das Abo — wer es vergisst, veraltet wieder still. `StammdatenIslandSyncTests`
> ist das Sicherheitsnetz und rendert mehrere Komponenten in **einem** `BunitContext` (geteilter
> DI-Container ⇒ geteilter Store und Notifier).

**`Save*` ist Upsert, nicht „Anlegen".** Create-Formulare müssen die Identität vorher prüfen
(`GetBankAsync`/`GetPartnerAsync`/`GetSubscriberAsync`), sonst überschreiben sie stillschweigend
bestehende Datensätze — beim Teilnehmer inklusive Status und Berechtigungen (#126).

**Filter-/Auswahllisten datengetrieben aufbauen**, nicht aus `Enum.GetValues`: Optionen ohne
Datenbestand führen nur in „keine Treffer" (siehe `GetTypeOptionsAsync`/`GetSeverityOptionsAsync`).

## Neue Seite/Komponente anlegen

1. Razor-Komponente unter `Components/` (bei Seite mit `@page`, an der Navigation registrieren).
2. Zustandszugriff über die vorhandenen Service-Abstraktionen (`IEmulatorStateProvider` /
   `ITransactionInspectorProvider` / `IMasterDataManager`), nicht direkt gegen Stores koppeln.
3. Beispieldaten-Seeding erweitern, wenn die Ansicht sonst leer bliebe.
4. Bei Stammdaten-Anzeige: Notifier abonnieren (siehe oben).
5. Seiten setzen ein `<PageTitle>` und tragen genau eine `<h1>` — `Routes.razor` fokussiert per
   `<FocusOnNavigate Selector="h1" />` darauf.

## Tests (bUnit)

- `tests/EBICO.Tests/Suite`: `BunitContext` + `Render(...)`.
- **Falle xUnit1051 unter `TreatWarningsAsErrors`:** bei Aufrufen, die einen `CancellationToken`
  akzeptieren, `TestContext.Current.CancellationToken` übergeben — sonst Build-Fehler.
- Services vor dem Rendern in den Test-DI registrieren.

## Definition of Done

Doku unter `docs/suite/` + Verlinkung in `docs/index.md`, Tests, ggf. ADR. Ablauf: `ebics-feature-workflow`.

## Quellen

- Code: `src/EBICO.Suite/{Components,Services,wwwroot}`, `src/EBICO.Server/State`
  (`IMasterDataManager`, `IMessageCaptureStore`, `IEventLog`).
- Doku: `docs/suite/ui-shell.md`, `docs/suite/stammdaten.md`, `docs/suite/transaktions-inspektor.md`,
  `docs/suite/schluessel-ansicht.md`. ADR: 0009 (Render-Modus/in-process), 0010 (QuestPDF),
  0021 (Message-Capture), 0031 (Änderungsbenachrichtigung zwischen den Inseln).
