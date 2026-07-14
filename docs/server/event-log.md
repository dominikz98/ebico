# Server: Ereignis-/Protokollspeicher (IEventLog)

> Umsetzung von **Issue #69** (Milestone M4 — Server: Transaction Engine). Diese Seite beschreibt den
> gemeinsamen, **append-only Ereignis-/Protokollspeicher** des Emulators: das Ereignismodell, die
> Store-Abstraktion, die Query-API, die verdrahteten Schreibpunkte und die zwei Sichten, die darüber
> gelesen werden.
>
> Bewusst **enthalten**: das Ereignismodell (`EbicsEvent` + Enums `EbicsEventType`/`EbicsEventSeverity`/
> `EbicsEventVisibility`), die Abstraktion `IEventLog` (Append + Query), der thread-sichere In-Memory-Default
> `InMemoryEventLog` (Ring-Puffer via `EbicoServerOptions.MaxEventLogEntries`), die Filter-API
> `EbicsEventQuery` (Kunde/Zeitraum/Typ/Sichtbarkeit/Limit), der **zentrale** Schreibpunkt in der
> [`EbicsRequestPipeline`](host.md) (ein Ereignis je Anfrage) und die **Lifecycle**-Schreibpunkte in den
> Transaktions-Engines ([Upload](upload-transaction.md)/[Download](download-transaction.md)).
> Bewusst **noch nicht**: die zwei **Projektionen** selbst — **HAC** (Customer Protocol, M5) und der
> **Suite-Inspektor** (M7) — konsumieren `IEventLog` nur, sind aber eigene Issues; eine **persistente**
> Implementierung (SQLite o. ä., [ADR-0015](../adr/0015-ereignis-protokollspeicher.md)); Ereignisse aus
> **Signatur/VEU** (die Verify-Stage ist ein No-Op, VEU existiert serverseitig noch nicht).

## Zweck

HAC und der Suite-Inspektor lesen **denselben** Ereignisstrom, erzeugen ihn aber nicht. Ohne eine
gemeinsame Quelle hätte HAC nichts zurückzugeben und die Suite nichts anzuzeigen — und jede Sicht baute
sonst ihr eigenes, divergierendes Log. `IEventLog` ist diese eine append-only Ablage: alle
Server-Komponenten **schreiben** relevante Ereignisse hinein, niemand mutiert oder löscht sie, und beide
Sichten sind reine **Projektionen** darüber. Diese Grundlage gehört vor M5 (HAC) und M7 (Inspektor).

## Modell

`EbicsEvent` (`EBICO.Server.State`) ist ein immutables `sealed record`. `Sequence` und `Timestamp` werden
vom Store beim Append vergeben — ein Schreiber liefert nur den fachlichen Inhalt.

| Feld | Typ | Bedeutung |
| --- | --- | --- |
| `Sequence` | `long` | Monotone, 1-basierte Reihenfolge; vom Store vergeben. Stabile Gesamtordnung, auch bei gleichem `Timestamp`. |
| `Timestamp` | `DateTimeOffset` | Zeitpunkt des Appends; aus dem injizierten `TimeProvider` gestempelt. |
| `Type` | `EbicsEventType` | Ereignisart (siehe unten). |
| `Severity` | `EbicsEventSeverity` | `Info` \| `Warning` \| `Error`. |
| `Visibility` | `EbicsEventVisibility` | `CustomerVisible` \| `Internal` — trennt die zwei Projektionen. |
| `HostId` / `PartnerId` / `UserId` | `HostId?` / `PartnerId?` / `UserId?` | Kunde/Teilnehmer (aus `EBICO.Core.Domain`); nullbar, weil nicht jedes Ereignis vollständig zuordenbar ist. |
| `OrderType` | `string?` | Order-Typ (z. B. `HPB`, `BTU`). |
| `TransactionId` | `string?` | Hex-Transaktions-ID bei Transaktionsereignissen. |
| `ReturnCode` | `EbicsReturnCode?` | Ergebnis; trägt `Code` + `SymbolicName` + `Kind` ([Returncode-Katalog](../protocol/return-codes.md)). |
| `Message` | `string` | Kurze, menschenlesbare Beschreibung. |

**`EbicsEventType`** (fokussierter Startsatz, erweiterbar): `RequestReceived` (zentral je Anfrage),
`UploadStarted`/`UploadCompleted`, `DownloadStarted`/`DownloadCompleted`, `ReceiptNegative`,
`TransactionEvicted`.

## Sichtbarkeit & Severity

**Sichtbarkeit** steuert, welche Sicht ein Ereignis sieht:

- `CustomerVisible` — für den Kunden relevant, von **HAC** ausgeliefert (und auch in der Suite sichtbar):
  Auftrag eingegangen (Initialisation/einphasige Order), Upload/Download gestartet/abgeschlossen, negative
  Quittung.
- `Internal` — nur für den Betreiber, **nur im Suite-Inspektor**: segmentweise Transfer-/Receipt-Schritte
  (Protokoll-Rauschen), Eviction verwaister Transaktionen, interne/technische Fehler.

Die Pipeline leitet die Werte je Anfrage automatisch ab: `EBICS_OK` → `Info`; `EBICS_INTERNAL_ERROR` →
`Error` + `Internal` (nie kundensichtbar); sonstige Rückweisungen → `Warning`. Ein `RequestReceived` einer
**Transfer-/Receipt**-Phase ist `Internal` (Segment-Rauschen), eine Initialisation bzw. einphasige Order
ist `CustomerVisible`.

## Query-API

`IEventLog.QueryAsync(EbicsEventQuery, …)` liefert die Treffer nach `Sequence` **aufsteigend**. Alle
Filter sind optional (`null` = kein Filter) und kombinieren per **UND**:

| Filter | Wirkung |
| --- | --- |
| `HostId` / `PartnerId` / `UserId` | Nur Ereignisse dieses Hosts/Kunden/Teilnehmers. |
| `Type` | Nur Ereignisse dieses Typs. |
| `Visibility` | Nur Ereignisse dieser Sichtbarkeit (HAC nutzt `CustomerVisible`). |
| `From` / `To` | Zeitfenster: `From` **inklusive**, `To` **exklusiv**. |
| `Limit` | Höchstens N (die frühesten nach `Sequence`); `null`/≤0 = unbegrenzt. |

## Schreibpunkte

**Zentral (ein Ereignis je Anfrage):** die [`EbicsRequestPipeline`](host.md) schreibt nach dem
Verarbeiten jeder Anfrage ein `RequestReceived` mit Subscriber (aus dem Static-Header —
Transfer/Receipt tragen nur die HostID), `OrderType`, `TransactionId` und dem finalen `ReturnCode`. Das
deckt das **Key-Management** (INI/HIA/HPB/HCA/HCS/SPR/HSA) mit ab.

**Lifecycle (semantische Transaktionsereignisse):** die Engines schreiben mit der vollen
Teilnehmer-Bindung der Transaktion:

- [Upload](upload-transaction.md): `UploadStarted` (Initialisation) und `UploadCompleted` (letztes Segment
  reassembliert).
- [Download](download-transaction.md): `DownloadStarted` (Initialisation), `DownloadCompleted` (positive
  Quittung `011000`), `ReceiptNegative` (negative Quittung `011001`, Daten wieder eingestellt).
- Beide: `TransactionEvicted` beim Idle-Timeout-Sweep des
  [`TransactionCleanupService`](transaction-recovery.md) (`Internal`).

> **Spec-Vorbehalt:** Signatur-/VEU-Ereignisse fehlen bewusst — die Verify-Stage ist ein No-Op und die
> verteilte elektronische Unterschrift ist serverseitig noch nicht implementiert.

## Zwei Projektionen

- **HAC (Customer Protocol, M5):** liest je Kunde und nur kundensichtbar —
  `QueryAsync(new EbicsEventQuery { PartnerId = …, Visibility = CustomerVisible })` — und mappt das
  Ergebnis spec-konform. Erzeugt **kein** eigenes Log.
- **Suite-Inspektor (M7):** liest **roh und global** über alle Kunden (ohne Sichtbarkeitsfilter), mit
  Live-Filtern (Kunde/Zeitraum/Typ/Severity) und Sprung Ereignis → Transaktion. Sieht auch die internen
  Details. Die Suite greift in-process auf den Store zu ([ADR-0009](../adr/0009-blazor-render-mode.md)).

## Beispiel-Ereignisse

| Seq | Type | Severity | Visibility | Partner/User | OrderType | ReturnCode | Message |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `RequestReceived` | Info | CustomerVisible | PARTNER01 / USER01 | INI | `EBICS_OK` | `INI → EBICS_OK` |
| 2 | `RequestReceived` | Info | CustomerVisible | PARTNER01 / USER01 | BTU | `EBICS_OK` | `BTU → EBICS_OK` |
| 3 | `UploadStarted` | Info | CustomerVisible | PARTNER01 / USER01 | BTU | `EBICS_OK` | Upload started (3 segment(s), …) |
| 4 | `RequestReceived` | Info | Internal | — / — (nur HostID) | — | `EBICS_OK` | `request → EBICS_OK` (Transfer) |
| 5 | `UploadCompleted` | Info | CustomerVisible | PARTNER01 / USER01 | BTU | `EBICS_OK` | Upload completed (…) |
| 6 | `RequestReceived` | Warning | CustomerVisible | PARTNER02 / USER09 | XYZ | `EBICS_UNSUPPORTED_ORDER_TYPE` | `XYZ → EBICS_UNSUPPORTED_ORDER_TYPE` |
| 7 | `TransactionEvicted` | Warning | Internal | PARTNER03 / USER02 | BTD | — | Download transaction evicted after idle timeout … |

HAC (für PARTNER01) sähe nur die Sequenzen 1, 2, 3, 5; der Inspektor sähe alle.

## Konfiguration

`EbicoServerOptions.MaxEventLogEntries` (Default `10000`) begrenzt den In-Memory-Log: bei Erreichen der
Obergrenze verwirft ein neuer Append das älteste Ereignis (Ring-Puffer). `0` = unbegrenzt (wächst bis zum
Prozess-Neustart). Die Sequenznummern wachsen unabhängig von der Eviction weiter.

## Persistenz

Der Default `InMemoryEventLog` hält nichts über den Prozess-Neustart hinaus — derselbe Ansatz wie der
übrige Server-Zustand ([ADR-0011](../adr/0011-server-stammdatenverwaltung.md)). Das Interface ist
**asynchron**, damit ein persistenter Store (SQLite o. ä.) später via
`TryAddSingleton<IEventLog, …>` vor `AddEbicoServer` eingehängt werden kann, **ohne** einen Aufrufer zu
ändern. Details und Abgrenzung: [ADR-0015](../adr/0015-ereignis-protokollspeicher.md).
