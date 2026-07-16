# 0021 — Message-Capture-Store (`IMessageCaptureStore`)

- Status: accepted
- Datum: 2026-07-16

## Kontext

Der Transaktions-Inspektor der Suite (M7, [#54](../suite/transaktions-inspektor.md)) soll pro
Transaktion die **Roh-XML** von Request und Response je Phase anzeigen. Diese XML entsteht nur
**transient** im Pipeline-Durchlauf: der Request liegt in `EbicsRequestContext.RequestXml`, die
Response in den serialisierten Antwort-Bytes; danach wird beides verworfen. Weder der
[`IEventLog`](0015-ereignis-protokollspeicher.md) (der trägt strukturierte Ereignisse, keine
Envelopes) noch die Transaktions-Stores (die tragen entschlüsseltes OrderData, keine XML) halten die
rohe Nachricht. Es fehlt eine Ablage für die verbatim Envelopes.

Zu entscheiden war: (a) wo die Roh-XML gehalten wird, (b) das Modell und die Store-Abstraktion, und
(c) wie der Speicher begrenzt wird.

## Entscheidung

1. **Eigener, transaktions-scoped Store** `IMessageCaptureStore` (Append + Get-by-TransactionId),
   **nicht** ein neues Feld am `EbicsEvent`. Begründung: das Event-Modell bleibt schlank (die
   HAC-Projektion würde die XML nie brauchen), und die Captures sind rein betreiberseitig
   (nur der Suite-Inspektor liest sie). Das Modell `CapturedMessage` ist ein immutables `sealed record`
   (ADR-0007-Stil) mit store-vergebener `Sequence`/`Timestamp`, `TransactionIdHex` (Key), `Phase`,
   optionaler `SegmentNumber`, Subscriber-Koordinaten, `RequestXml`/`ResponseXml` (als **Text**, nicht
   `byte[]`) und vollem `EbicsReturnCode`.
2. **Ein zentraler Schreibpunkt in der [`EbicsRequestPipeline`](../server/host.md)** — genau nach der
   Serialisierung der Antwort, dem einzigen Punkt, an dem Request-XML, Response-XML, die aufgelöste
   Transaktions-ID/Phase und der finale Returncode gleichzeitig vorliegen. Erfasst wird **nur**, wenn
   eine Transaktions-ID auflösbar ist. **Key-Management-Orders** (INI/HIA/HPB) tragen keine
   Transaktions-ID und werden bewusst **nicht** erfasst — sie erscheinen weiter im Event-Log, nur
   ohne Roh-XML.
3. **In-Memory-Default (`InMemoryMessageCaptureStore`), pluggbar via `TryAddSingleton`** — exakt der
   Store-Weg aus [ADR-0011](0011-server-stammdatenverwaltung.md)/[ADR-0015](0015-ereignis-protokollspeicher.md).
   Speicherbegrenzung auf zwei Achsen: **Ring-Puffer** über alle Captures
   (`EbicoServerOptions.MaxMessageCaptureEntries`, Default 200) und **Kürzung** je XML-Dokument
   (`EbicoServerOptions.MaxCapturedMessageBytes`, Default 256 KiB). Das async Interface hält den Weg
   zu einem persistenten Store (SQLite o. ä.) offen, ohne einen Aufrufer zu ändern.

## Konsequenzen

- Der Inspektor bekommt die Roh-XML als reine **Projektion** über `GetAsync(transactionIdHex)`;
  parallel zum Event-Log entsteht kein zweites Logsystem, nur eine transaktions-scoped Beilage.
- Die Kürzung ist eine reine **Anzeige**-Verkürzung (mit `*Truncated`-Flag); das autoritative,
  entschlüsselte OrderData kommt aus dem Transaktions-Store, nicht aus einem gekürzten Capture.
- Roh-XML für Key-Management ist damit **nicht** abgedeckt — falls später gewünscht, braucht es einen
  eigenen (nicht transaktions-scoped) Schlüssel; bewusst zurückgestellt.
- Wie der übrige Server-Zustand geht der In-Memory-Store beim Neustart verloren; ein persistenter
  Store ist Folge-Arbeit (derselbe Backlog-Punkt wie beim `IEventLog`, ADR-0015).

## Alternativen

- **Roh-XML als Felder am `EbicsEvent`.** Verworfen: bläht jedes Ereignis auf (auch die vielen ohne
  Transaktionsbezug), belastet die HAC-Projektion und mischt strukturierte Ereignisse mit Envelopes.
- **Roh-XML an der Transaktion (`UploadTransaction`/`DownloadTransaction`).** Verworfen: die Engines
  sehen die Envelope-XML nicht (nur die Pipeline), und die Transaktionsobjekte werden nach dem
  Idle-Timeout evictet — die Roh-XML soll die Transaktion überdauern (bis zum Ring-Puffer-Limit).
- **Kein persistenter Speicher, Roh-XML nur zur Laufzeit „durchreichen".** Verworfen: der Inspektor
  liest asynchron, lange nach dem Request; ohne Ablage gäbe es nichts anzuzeigen.
