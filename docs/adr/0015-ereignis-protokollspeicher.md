# 0015 — Ereignis-/Protokollspeicher (`IEventLog`)

- Status: accepted
- Datum: 2026-07-14

## Kontext

Zwei geplante Features lesen denselben serverseitigen Ereignisstrom, erzeugen ihn aber nicht: die
kundenseitige **HAC**-Protokoll-Order (M5) und der **Suite-Inspektor** (M7). Ohne eine gemeinsame
Quelle hätte HAC nichts zurückzugeben und die Suite nichts anzuzeigen; jede Sicht würde sonst ihr
eigenes, divergierendes Log aufbauen. Gebraucht wird **eine** append-only Ablage, in die alle
Server-Komponenten relevante Ereignisse schreiben (Auftrag eingegangen, Returncode vergeben,
Transaktion abgeschlossen, Key-Mgmt-Aktion …), mit genug Struktur für beide Projektionen.

Zu entscheiden war: (a) die Modellierung des Ereignisses und die Store-Abstraktion, (b) der
Persistenz-Ansatz, und (c) wo Ereignisse geschrieben werden.

## Entscheidung

1. **Ein append-only Ereignismodell** `EbicsEvent` (immutables `sealed record`, ADR-0007-Stil) mit:
   store-vergebener monotoner `Sequence` + gestempeltem `Timestamp`, `Type`/`Severity`/`Visibility`
   (Enums), nullbaren Subscriber-Koordinaten (`HostId`/`PartnerId`/`UserId` aus `EBICO.Core.Domain`),
   `OrderType`, `TransactionId` (Hex), vollem `EbicsReturnCode` (ADR-0012, trägt `Code`+`SymbolicName`)
   und `Message`. **Sichtbarkeit** (`CustomerVisible` vs. `Internal`) ist das Feld, an dem sich die
   beiden Projektionen trennen.
2. **`IEventLog` = Append + Query**, asynchron, pluggbar via `TryAddSingleton` — **exakt der Store-Weg**
   aus [ADR-0011](0011-server-stammdatenverwaltung.md). Query filtert nach Kunde/Zeitraum/Typ/
   Sichtbarkeit (`EbicsEventQuery`, `From` inklusive / `To` exklusiv, optionales `Limit`).
3. **In-Memory-Default (`InMemoryEventLog`), Persistenz zurückgestellt.** Das ist „derselbe
   Persistenz-Ansatz wie der übrige Server-Zustand": In-Memory, thread-sicher, mit **Ring-Puffer**
   (`EbicoServerOptions.MaxEventLogEntries`, Default 10 000) als Speicher-Obergrenze. Das async
   Interface ist so gebaut, dass ein persistenter Store (SQLite o. ä.) später **nur die Implementierung
   ersetzt**, ohne Aufrufer zu ändern.
4. **Schreibpunkte: zentral + Lifecycle.** Ein **zentraler** Punkt in der
   [`EbicsRequestPipeline`](../server/host.md) schreibt je Anfrage ein `RequestReceived`-Ereignis
   (Subscriber/OrderType/Phase/Returncode) — das deckt auch das Key-Management ab. Die
   Transaktions-Engines ([#32](0013-upload-transaktions-engine.md)/[#33](0014-download-transaktions-engine.md))
   ergänzen **semantische Lifecycle-Ereignisse** (Upload/Download gestartet/abgeschlossen, negative
   Quittung, Eviction im Hintergrund-Sweep), da diese eine Transaktion über mehrere Requests spannen
   bzw. requestlos im Cleanup entstehen.

## Konsequenzen

- HAC und Suite werden reine **Projektionen** über `QueryAsync` — HAC mit
  `{ PartnerId, Visibility = CustomerVisible }`, die Suite roh/global. Kein paralleles Logsystem.
- Der EventLog ist der **erste** Baustein mit echter Persistenz-Perspektive; bis ein SQLite-Store
  existiert, geht der Log wie der übrige Server-Zustand beim Neustart verloren — für den Emulator
  akzeptabel. Ein konkreter persistenter Store ist Folge-Arbeit (siehe Backlog im ADR-Index).
- Segmentweise Transfer-/Receipt-Schritte sind als `Internal` markiert, damit die HAC-Sicht nicht mit
  Protokoll-Rauschen zuläuft; interne Fehler (`EBICS_INTERNAL_ERROR`) sind nie kundensichtbar.
- **VEU/ES- und X002-Signaturprüfung** sind noch nicht verdrahtet (Verify-Stage ist ein No-Op, VEU
  existiert serverseitig nicht) — entsprechende Ereignisse folgen, sobald diese Schritte real werden.

## Alternativen

- **`ILogger`/strukturiertes Logging** als Ereignisquelle. Verworfen: Logs sind für Menschen/Sinks,
  nicht abfragbar je Kunde/Zeitraum und tragen keine Sichtbarkeits-/Returncode-Semantik, die HAC braucht.
- **Sofort SQLite** implementieren. Verworfen für diesen PR: macht den EventLog zum einzigen
  persistenten Store (Asymmetrie zum sonst flüchtigen Zustand) und zieht eine neue Abhängigkeit + eigene
  Persistenz-ADR nach sich. Das async Interface hält den Weg offen, ohne ihn jetzt zu gehen.
- **Granulare Schreibpunkte in jedem Key-Mgmt-Handler.** Verworfen: der zentrale Pipeline-Punkt trägt
  bereits Subscriber + OrderType + Returncode; verstreute Aufrufe über ~15 Handler wären redundant und
  fehleranfällig.
