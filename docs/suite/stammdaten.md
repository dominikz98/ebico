# Suite: Stammdaten-Verwaltung (Banken / Partner / Teilnehmer)

> Umsetzung von **Issue #53** (Milestone M7 — Suite). Baut auf dem UI-Grundgerüst
> ([#52](ui-shell.md)) auf und ist die Schreib-Oberfläche über die server-seitige
> **Stammdatenverwaltung** aus [#30](../server/master-data.md): sie treibt den
> `IMasterDataManager` (`EBICO.Server`) **in-process** an, gemäß
> [ADR-0009](../adr/0009-blazor-render-mode.md).

## Zweck

Die Seite `/stammdaten` verwaltet die Stammdaten des Emulators: **Banken** (Kreditinstitute /
EBICS-Hosts), **Partner** (Kunden) und **Teilnehmer**. Sie deckt Anlegen, Bearbeiten und
(kaskadierendes) Löschen ab, macht den **Teilnehmer-Status** sichtbar/änderbar und erlaubt das
Editieren der **Berechtigungen** eines Teilnehmers. Sie löst die read-only Übersicht des
Grundgerüsts (#52) ab.

## Anbindung: in-process statt HTTP

Statt einer eigenen HTTP-API (die es server-seitig als Admin-API zwar gibt, siehe
[master-data.md](../server/master-data.md)) nutzt die Suite den State-Layer aus `EBICO.Server`
**direkt über DI** — die von [ADR-0009](../adr/0009-blazor-render-mode.md) vorgesehene
In-Process-Anbindung. Dazu referenziert `EBICO.Suite` jetzt `EBICO.Server` (Suite → Server → Core).

```csharp
// Program.cs — Server-Zustand in-process
builder.Services.AddSingleton<IEbicsStateStore, InMemoryEbicsStateStore>();
builder.Services.AddSingleton<IMasterDataManager, MasterDataManager>();
builder.Services.AddSingleton<SampleEmulatorStateProvider>();          // Sample-Daten + Schlüssel
builder.Services.AddScoped<IEmulatorStateProvider, EmulatorStateProvider>(); // Live-Read-Model
…
var app = builder.Build();
await EmulatorStateSeeder.SeedAsync(app.Services);   // Sample-Stammdaten in den In-Memory-Store
```

| Typ | Rolle |
| --- | --- |
| `IMasterDataManager` (Server) | Schreib-/Verwaltungslogik: CRUD, referentielle Integrität, Kaskaden, Lifecycle, Permissions |
| `EmulatorStateProvider` | Live-Read-Model: `GetBanks/Partners/SubscribersAsync` aus dem `IEbicsStateStore`; `GetKeysAsync` weiterhin aus den Sample-Daten |
| `EmulatorStateSeeder` | füllt den (leeren) In-Memory-Store beim Start mit Beispiel-Stammdaten (Banken → Partner → Teilnehmer) |

Der Store ist In-Memory (Zustand geht bei Neustart verloren). Schlüsselmaterial ist noch kein Teil
des Server-Stores (späteres M3/M4-Issue), daher liefert `GetKeysAsync` weiterhin die
deterministischen Beispiel-Schlüssel für die Schlüssel-Ansicht ([#55](schluessel-ansicht.md)).

## Render-Modus

Die Seite selbst ist **Static SSR**; die drei Verwaltungsbereiche sind **interaktive Inseln**
(`<BankManager @rendermode="InteractiveServer" />` usw.), der Render-Modus wird am Einbettungsort
gesetzt (ADR-0009, „Interaktivität pro Komponente"). Formulare nutzen einfaches Bootstrap mit
`@bind`/`@onclick` und melden Ergebnisse über Bootstrap-Alerts zurück — keine Exceptions in der UI.

## Konsistenz zwischen den Inseln (#126)

Die drei Inseln sind **eigenständige Komponenten mit je eigener Zustandskopie**, schreiben aber durch
denselben `IMasterDataManager` — und die Beziehungen kaskadieren. Ohne Benachrichtigung entwertet
deshalb jede Mutation in einer Insel die anderen beiden still (neue Bank fehlt in den Auswahlfeldern,
kaskadierend gelöschte Zeilen bleiben als Karteileichen stehen). Dafür gibt es
**`IMasterDataChangeNotifier`** ([ADR-0031](../adr/0031-stammdaten-inseln-aenderungsbenachrichtigung.md)):

```csharp
// Program.cs — Singleton, wie die Stores dahinter
builder.Services.AddSingleton<IMasterDataChangeNotifier, MasterDataChangeNotifier>();
```

Regeln für jede Komponente, die Stammdaten anzeigt:

1. In `OnInitializedAsync` **abonnieren**, in `Dispose` das Abo **zurückgeben**
   (`@implements IDisposable`).
2. Nach **jeder** erfolgreichen Mutation `await Changes.NotifyChangedAsync()`.
3. Im Handler über **`InvokeAsync`** auf den eigenen Renderer zurückwechseln — die Benachrichtigung
   kommt auf dem Thread des auslösenden Circuits an, nicht auf dem eigenen.
4. Nach dem Neuladen **transiente UI-Zustände prüfen**: ein offenes Formular darf keine gelöschte
   Bank mehr anbieten, eine Löschbestätigung für einen kaskadierten Datensatz ist gegenstandslos, ein
   Detailbereich ohne Datensatz schließt sich. Neuladen allein repariert nur die Tabellen.

> **Wer das Abo vergisst, veraltet wieder still** — es gibt dafür keinen Wächter außer den Tests in
> `StammdatenIslandSyncTests`.

Weil der Notifier ein Singleton ist, konvergieren auch **mehrere Browser-Sitzungen**: der Zustand
dahinter ist prozessweit geteilt, die Benachrichtigung ist es damit ebenso.

## Anlegen ist nicht Überschreiben (#126)

Die `Save*`-Operationen des Managers sind **idempotente Upserts**
([master-data.md](../server/master-data.md)) — richtig für die API, aber gefährlich hinter einem
Formular, das „Anlegen" heißt. Ein `SaveSubscriberAsync(new Subscriber(...))` auf eine bereits
belegte Identität setzte den Teilnehmer auf `New` zurück und verwarf alle Berechtigungen, gemeldet mit
einer grünen Erfolgsmeldung. Die Create-Pfade prüfen die Identität deshalb **vorher**
(`GetBankAsync`/`GetPartnerAsync`/`GetSubscriberAsync`) und weisen die Kollision mit einem Hinweis auf
„Bearbeiten" bzw. „Details" ab. Die Edit-Pfade sind unberührt — dort *ist* Überschreiben gewollt.

Die Mehrmandanten-Semantik bleibt erhalten: derselbe `PartnerID` an einer anderen Bank bzw. dieselbe
`UserID` bei einem anderen Partner ist eine **andere** Identität und wird nicht als Kollision gewertet.

## Sortierung

Der Store liefert Dictionary-Reihenfolge (`_banks.Values.ToArray()`), also keine zugesagte Ordnung —
in der UI sprangen Zeilen dadurch beim Anlegen/Löschen an unvorhersehbare Stellen. Die Komponenten
sortieren daher selbst (Banken nach `HostID`, Partner nach `(HostID, PartnerID)`, Teilnehmer nach
`(HostID, PartnerID, UserID)`, ordinal). Der Store bleibt bewusst „dumm" — die Sortierung ist eine
Darstellungsfrage.

## Aufbau

| Komponente | Inhalt | Operationen |
| --- | --- | --- |
| `BankManager` | Liste HostID / Name / Versionen | Anlegen, Bearbeiten (HostID read-only), Löschen (**Kaskade**: Partner + Teilnehmer) |
| `PartnerManager` | Liste HostID / PartnerID / Name | Anlegen (Bank per Dropdown), Bearbeiten (Name), Löschen (**Kaskade**: Teilnehmer) |
| `SubscriberManager` | Liste HostID / PartnerID / UserID / Status / Typ + Detail | Anlegen (Bank+Partner per Dropdown), Status ändern, Berechtigungen editieren, Löschen |

Eingaben werden über `HostId/PartnerId/UserId/SystemId.TryCreate` validiert (freundliche Meldung
statt Exception). Beim Bearbeiten sind die ID-Felder gesperrt (sie sind die Store-Keys —
Umbenennen = neu anlegen). Partner/Teilnehmer werden über **Dropdowns** aus den vorhandenen
Banken/Partnern erzeugt, sodass keine verwaisten Datensätze entstehen (der Manager würde
referenzverletzende Anlagen ohnehin mit `UnknownBankException`/`UnknownPartnerException` abweisen).

## Teilnehmer-Status

Die Statuswechsel gehen über `IMasterDataManager.TransitionSubscriberAsync`, das den
Lebenszyklus in `Subscriber.Transition` validiert. Die UI zeigt nur die **erlaubten** Übergänge
des aktuellen Zustands als Buttons:

| Aktueller Status | Aktionen |
| --- | --- |
| `New` | Initialisieren (→ `Initialized`), Sperren (→ `Suspended`) |
| `Initialized` | Aktivieren (→ `Ready`), Sperren (→ `Suspended`) |
| `Ready` | Sperren (→ `Suspended`) |
| `Suspended` | Reaktivieren (→ `Ready`) |

Ein unzulässiger Übergang (`InvalidSubscriberStateTransitionException`) wird defensiv abgefangen
und als Fehler-Alert angezeigt.

## Berechtigungen

Im Detailbereich eines Teilnehmers lassen sich Berechtigungen (Auftragstyp/BTF × Signaturklasse
`E`/`A`/`B`/`T`) als Zeilen hinzufügen/entfernen; „Berechtigungen speichern" ersetzt die gesamte
Menge über `SetPermissionsAsync`. OrderType/BTF ist derzeit ein freier String (typisiertes Modell
→ M5).

## EBICS-Versionsbezug

Identitäten (ID-Muster/-Länge) und Signaturklassen sind über **H003/H004/H005 identisch**; die
Stammdatenverwaltung ist damit versionsunabhängig. `Bank.SupportedVersions` (Checkboxen im
Bank-Formular) hält je Host die angebotenen Versionen (Default: alle).

## Tests

`tests/EBICO.Tests/Suite/` (bUnit + xUnit v3 + AwesomeAssertions; die Komponententests verdrahten
den **echten** `MasterDataManager` über einen `InMemoryEbicsStateStore`):

- `EmulatorStateProviderTests` — die Read-Model-Brücke liefert Store-Inhalt und spiegelt
  Live-Mutationen; `GetKeysAsync` delegiert an die Sample-Schlüssel.
- `EmulatorStateSeederTests` — der Seeder legt die Beispiel-Stammdaten in Reihenfolge an und ist
  idempotent.
- `BankManagerTests` — Rendern, Anlegen, ungültige HostID → Warnung, Löschen.
- `PartnerManagerTests` — Anlegen über Bank-Dropdown, „ohne Bank"-Sperre, Löschen.
- `SubscriberManagerTests` — Anlegen über abhängige Dropdowns, Status-Übergang, Berechtigung
  hinzufügen/speichern, Löschen.
- `MasterDataChangeNotifierTests` — der Broadcast-Kontrakt: jeder Abonnent wird erreicht, `Dispose`
  meldet wirklich ab (und ist idempotent), ein fehlschlagender Abonnent stoppt die übrigen nicht.
- `StammdatenIslandSyncTests` — die #126-Regression. Mehrere Komponenten in **einem**
  `BunitContext` teilen den DI-Container und damit Store und Notifier, was den Insel-Aufbau der Seite
  nachbildet: neue Bank erscheint in beiden Auswahlfeldern (auch in einem *bereits geöffneten*
  Formular), gelöschte Bank verschwindet daraus, Kaskaden räumen die Geschwister-Tabellen, ein
  Detailbereich über einem kaskadierten Teilnehmer schließt sich, und die Tabellen sind sortiert.
- `StammdatenCreateCollisionTests` — Anlegen auf eine belegte Identität wird abgewiesen und lässt
  Status/Berechtigungen unangetastet; Bearbeiten speichert weiterhin; gleiche `PartnerID`/`UserID` bei
  anderer Bank/anderem Partner bleibt erlaubt.

## Verwandtes

- [UI-Grundgerüst & Navigation](ui-shell.md)
- [Server: Stammdatenverwaltung (#30)](../server/master-data.md) — die genutzte Manager-/Store-Schicht
- [Domänenmodell](../protocol/domain-model.md) — Aggregate, IDs, Berechtigungen, Zustände
- [ADR-0009 — Blazor Render-Modus (In-Process-Zustand)](../adr/0009-blazor-render-mode.md)
- [ADR-0031 — Änderungsbenachrichtigung zwischen den Stammdaten-Inseln](../adr/0031-stammdaten-inseln-aenderungsbenachrichtigung.md)
