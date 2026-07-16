# Suite: Transaktions-Inspektor

> Umsetzung von **Issue #54** (Milestone M7 — Suite). Baut auf dem UI-Grundgerüst
> ([#52](ui-shell.md)) auf und ist die Betreiber-/Entwicklersicht über den serverseitigen
> **Ereignis-/Protokollspeicher** ([#69](../server/event-log.md)) und die Transaktions-Stores
> aus `EBICO.Server`: sie liest sie **in-process** gemäß [ADR-0009](../adr/0009-blazor-render-mode.md).
> Die Roh-XML-Ansicht speist sich aus einem neuen **Message-Capture-Store**
> ([ADR-0021](../adr/0021-message-capture-store.md)).

## Zweck

Die Seite `/transaktionen` zeigt die Upload-/Download-Transaktionen des Emulators — laufend wie
abgeschlossen — und pro Transaktion die **Roh-XML** (Request/Response je Phase), das **entschlüsselte
OrderData** und die **Returncodes**. Darunter liegt die **globale Protokollansicht**: der ungefilterte
Ereignisstrom über **alle** Kunden (inklusive betreiberinterner Ereignisse), mit Live-Filtern und
Sprung von einem Ereignis zur zugehörigen Transaktion.

Sie ist die zweite Projektion über den `IEventLog` (die erste ist die kundenseitige HAC-Order, M5) —
im Unterschied zu HAC liest der Inspektor **roh und global**, ohne Sichtbarkeitsfilter.

## Anbindung: in-process statt HTTP

Die Suite ist ein **eigenständiger Prozess** (sie hostet keine EBICS-Pipeline). Wie bei den
Stammdaten registriert sie die benötigten `EBICO.Server`-Stores direkt und liest sie über einen
Read-Model-Provider — die von [ADR-0009](../adr/0009-blazor-render-mode.md) vorgesehene
In-Process-Anbindung. Da kein Live-Verkehr entsteht, werden Beispiel-Transaktionen geseedet.

```csharp
// Program.cs — Transaktions-/Ereignis-Zustand in-process (ADR-0009)
builder.Services.AddOptions<EbicoServerOptions>();
builder.Services.AddSingleton(TimeProvider.System);              // von Event-Log/Capture-Store benötigt
builder.Services.AddSingleton<IEventLog, InMemoryEventLog>();
builder.Services.AddSingleton<IUploadTransactionStore, InMemoryUploadTransactionStore>();
builder.Services.AddSingleton<IDownloadTransactionStore, InMemoryDownloadTransactionStore>();
builder.Services.AddSingleton<IMessageCaptureStore, InMemoryMessageCaptureStore>();
builder.Services.AddScoped<ITransactionInspectorProvider, TransactionInspectorProvider>();
…
var app = builder.Build();
await TransactionInspectorSeeder.SeedAsync(app.Services);        // Beispiel-Transaktionen/-Ereignisse/-Captures
```

| Typ | Rolle |
| --- | --- |
| `IEventLog` (Server) | Ereignisstrom; **Quelle der Transaktionsliste** (abgeschlossene Transaktionen verlassen die Stores) und der globalen Protokollansicht |
| `IUploadTransactionStore` / `IDownloadTransactionStore` | Anreicherung der residenten Transaktion: Segmentzahl und **entschlüsseltes OrderData** (`UploadTransaction.OrderData` / `DownloadTransaction.OrderDataPlaintext`) |
| `IMessageCaptureStore` (Server) | Roh-XML (Request/Response je Phase), keyed nach Transaktions-ID ([ADR-0021](../adr/0021-message-capture-store.md)) |
| `TransactionInspectorProvider` | Read-Model: fügt Event-Log, Stores und Captures zu UI-DTOs zusammen |
| `TransactionInspectorSeeder` | füllt die (leeren) In-Memory-Stores beim Start mit Beispiel-Transaktionen |

> **Grenze:** In dieser eigenständigen Ausprägung zeigt der Inspektor geseedete Daten. Echte,
> prozessübergreifende Live-Inspektion setzt einen persistenten, geteilten Store voraus
> (SQLite o. ä., [ADR-0015](../adr/0015-ereignis-protokollspeicher.md)) — Folgethema.

## Render-Modus

Die Seite selbst ist **Static SSR**; der Inspektor ist **eine** interaktive Insel
(`<TransactionInspector @rendermode="InteractiveServer" />`, ADR-0009). Der gesamte Zustand
(ausgewählte Transaktion, aktiver Tab, Filter) lebt in dieser einen Insel/Circuit, sodass der Sprung
„Ereignis → Transaktion" ohne Insel-übergreifende Kommunikation funktioniert.

## Aufbau

| Bereich | Inhalt |
| --- | --- |
| Transaktionsliste (`#tx-list`) | Status-Badge (laufend/abgeschlossen/fehlgeschlagen/evictet), Richtung, OrderType, Kunde/Teilnehmer, Segmente, ID, letzter Returncode, „Details" |
| Detailansicht (`#tx-detail`) | Tabs **Roh-XML** (Request/Response je Phase, `#tab-rawxml`), **OrderData** (Text/Hex, `#tab-orderdata`), **Ereignisverlauf** (`#tab-events`) |
| Globales Protokoll (`#event-log`) | ungefilterte Ereignisliste über alle Kunden mit Live-Filtern und Sprung zur Transaktion |

## Transaktionsliste & Status

Die Liste wird **aus dem Event-Log rekonstruiert** (Gruppierung nach `TransactionId`), weil
abgeschlossene Transaktionen die Stores nach dem Idle-Timeout verlassen. Der Status wird aus den
Ereignistypen abgeleitet: `TransactionEvicted` → **Evictet**; `Upload/DownloadCompleted` →
**Abgeschlossen**; eine Rückweisung (Severity ≥ Warning bzw. negative Quittung) → **Fehlgeschlagen**;
sonst **Laufend**. Solange die Transaktion **resident** ist, ergänzt der Provider Segmentzahl und
entschlüsseltes OrderData aus dem jeweiligen Store.

## Roh-XML & OrderData

- **Roh-XML** kommt aus dem `IMessageCaptureStore`: pro Transaktionsphase ein Request/Response-Paar,
  in `<pre>`-Blöcken **HTML-escaped** dargestellt (Blazor `@`-Interpolation, kein `MarkupString` →
  kein XSS). Übergroße Dokumente werden serverseitig gekürzt (Hinweis in der UI).
- **OrderData** ist bereits **entschlüsselt und dekomprimiert** (kein Base64): ein reiner
  Dokumenten-Bytestrom (pain.001/camt/MT). Eine Text/Binär-Heuristik entscheidet Text- vs.
  Hex-Darstellung; die volle Byte-Länge wird angezeigt. Nicht mehr residente (oder noch nicht
  abgeschlossene) Transaktionen haben kein OrderData.

## Globale Protokollansicht & Filter

Die Ereignisliste liest `IEventLog.QueryAsync` **ohne** Sichtbarkeitsfilter (auch `Internal`-Rauschen).
Live-Filter: **Kunde** (Partner-Dropdown), **Typ** (`EbicsEventType`), **Severity** und **Zeitraum**
(`Von`/`Bis`, UTC). Kunde/Typ/Zeitraum werden an `EbicsEventQuery` durchgereicht; **Severity wird
clientseitig** nachgefiltert, da `EbicsEventQuery` keine Severity-Dimension trägt. Jede Ereigniszeile
mit Transaktions-ID hat einen „→ Transaktion"-Sprung, der die Detailansicht öffnet.

## Grenzen

- **Key-Management-Orders** (INI/HIA/HPB/…) tragen keine Transaktions-ID und werden daher **nicht**
  roh erfasst — sie erscheinen weiterhin im globalen Protokoll, nur ohne Roh-XML-Tab.
- Kein prozessübergreifender Live-Zustand (siehe oben, ADR-0015).

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; bUnit für die UI):

- `Server/InMemoryMessageCaptureStoreTests` — Sequence/Timestamp, Ring-Puffer, keyed Lookup, Kürzung.
- `Server/MessageCaptureWritePointTests` — die Pipeline erfasst Init+Transfer roh; INI (ohne TxId) nicht.
- `Suite/TransactionInspectorProviderTests` — Rekonstruktion/Status/Kind, Filter (inkl. Severity
  clientseitig), OrderData resident vs. `null`, Kundenoptionen.
- `Suite/TransactionInspectorTests` — bUnit: Liste + Status-Badges, Detail-Tabs (Roh-XML/OrderData),
  Live-Severity-Filter, Sprung Ereignis→Transaktion.
- `Suite/TransactionInspectorSeederTests` — der Seeder füllt Log/Stores/Captures und ist idempotent.

## Verwandtes

- [UI-Grundgerüst & Navigation](ui-shell.md)
- [Server: Ereignis-/Protokollspeicher (#69)](../server/event-log.md) — die gemeinsame Ereignisquelle
- [Server: Host & Pipeline](../server/host.md) — der Capture-Schreibpunkt in der `EbicsRequestPipeline`
- [ADR-0009 — Blazor Render-Modus (In-Process-Zustand)](../adr/0009-blazor-render-mode.md)
- [ADR-0015 — Ereignis-/Protokollspeicher](../adr/0015-ereignis-protokollspeicher.md)
- [ADR-0021 — Message-Capture-Store](../adr/0021-message-capture-store.md)
