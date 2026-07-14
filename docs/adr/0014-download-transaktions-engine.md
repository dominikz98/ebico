# 0014 — Download-Transaktions-Engine, -Speicher & Datenbereitstellung

- Status: accepted
- Datum: 2026-07-13

## Kontext

Nach der [Upload-Transaktion](0013-upload-transaktions-engine.md) (#32) ist der EBICS-**Download**
(#33) die zweite mehrphasige Transaktion — und die erste in **Senderichtung** (Server→Client). Sie
hat **drei** Phasen: **Initialisation** (Daten bereitstellen, komprimieren, E002-verschlüsseln,
segmentieren, Transaction-ID-Vergabe, erstes Segment), **Transfer** (restliche Segmente) und
**Receipt** (Client quittiert). Gegenüber dem Upload waren drei Punkte neu zu entscheiden:

1. **Routing-Kollision Upload ↔ Download.** Ein Transfer-Request trägt nur die `TransactionID`,
   keinen Order-Typ. Die Pipeline muss ihn der **richtigen** Engine zuordnen — die
   [ADR-0013-Regel](0013-upload-transaktions-engine.md) („`TransactionID` vorhanden → Upload") würde
   Download-Transfers fälschlich an die Upload-Engine leiten. Zusätzlich ist die **Receipt**-Phase
   neu (Upload kennt sie nicht).
2. **Herkunft der Order-Data** („Datenbereitstellung serverseitig"). Es gab bisher **keinen**
   Auftragsdatenspeicher — nur Schema-Bindings.
3. **Wirkung der Quittung.** Positive vs. negative Quittung müssen sich auswirken (`011000` vs.
   `011001`) — die Frage war, ob nur der Returncode oder auch der Datenzustand betroffen ist.

## Entscheidung

1. **Dedizierte Engine für alle drei Phasen**, analog ADR-0013:
   `IDownloadTransactionEngine`/`DownloadTransactionEngine` mit `BeginDownloadAsync` /
   `ContinueDownloadAsync` / `AcknowledgeReceiptAsync` und eigenem Ergebnistyp
   (`DownloadTransactionResult` + `DownloadSegmentPayload`). Die Upload-Engine und die Single-Shot-
   Handler bleiben unangetastet. Eigener Speicher `IDownloadTransactionStore` (Default
   `InMemoryDownloadTransactionStore`), thread-sicher, pluggbar via `TryAddSingleton`, **keyed auf
   `Convert.ToHexString(TransactionID)`**.
2. **Routing per Store-Zugehörigkeit statt Order-Typ** (Kern der Entscheidung). In der Pipeline, vor
   dem Resolver, in fester Reihenfolge:
   - `phase=Receipt` → **immer** Download (Uploads haben keine Receipt-Phase);
   - Transfer / `TransactionID` vorhanden → `_downloadEngine.OwnsTransaction(id)` entscheidet: Treffer
     → Download-Transfer, sonst Fallback auf den Upload-Transfer (der bei echt unbekannter ID
     `091101` liefert);
   - `phase=Initialisation` → per Order-Typ: **FUL/BTU** → Upload, **FDL/BTD** → Download.
   16-Byte-Zufalls-IDs machen Store-Kollisionen praktisch ausgeschlossen.
3. **Provider-Abstraktion + Admin-API für die Datenbereitstellung.** `IDownloadDataProvider` (Default
   `InMemoryDownloadDataProvider`) hält je (Teilnehmer, Order-Typ) eine **FIFO-Queue** von
   Klartext-Order-Data; die Initialisation entnimmt das nächste Element (leer → `090005`). Eingestellt
   werden Daten über die bestehende [Admin-API](0011-server-stammdatenverwaltung.md)
   (`POST …/downloads/{orderType}`), analog zur Stammdatenverwaltung. Ein echter Datenspeicher ist via
   `TryAddSingleton` austauschbar.
4. **Verbrauchssemantik.** Die Initialisation entnimmt die Daten. Eine **positive** Quittung
   (`011000`) lässt sie verbraucht; eine **negative** (`011001`) stellt sie wieder ein. Beide
   Quittungscodes sind **technisch** → Header (via `EbicsReturnCode.Kind`). Es waren **keine** neuen
   Returncodes nötig — der Katalog aus [ADR-0012](0012-returncode-katalog.md) enthält sie bereits.

## Konsequenzen

- Upload und Download teilen sich Pipeline und Response-Factory (neue `BuildDownloadResponse` neben
  `BuildTransactionResponse`), bleiben aber als Engines/Stores **getrennt** (hohe Kohäsion). Der
  Download ist der erste produktive Nutzer von `EbicsSegmentation.Split` und `EncryptionE002.Encrypt`
  in einer Transaktion.
- Das `OwnsTransaction`-Routing hält den Store gekapselt (die Pipeline spricht nur Engines an) und
  bleibt für die Key-Management-Handler unverändert.
- Positive/negative Quittungen sind **verhaltenswirksam** und damit testbar (erneuter Download nach
  positiv → `090005`, nach negativ → dieselben Daten). Die Emulator-Nutzung ohne Code (nur Admin-API)
  ist möglich.
- Der In-Memory-Provider/-Store persistiert nicht und evictet nicht — verwaiste Transaktionen (kein
  Receipt) halten die entnommenen Daten „in Bearbeitung" bis zum Neustart; **Eviction/TTL/Recovery ist
  #35**. Für den Emulator akzeptabel.
- Die Antwort bleibt **unsigniert** (X002 = M4); Details in
  [docs/server/download-transaction.md](../server/download-transaction.md).

## Alternativen

- **Upload-Engine erweitern statt zweite Engine.** Verworfen: Init/Transfer/Receipt-Semantik und
  Zustand (fertig segmentierter Sendepuffer vs. Empfangs-Segmentpuffer) unterscheiden sich zu stark;
  zwei fokussierte Engines sind klarer und spiegeln ADR-0013.
- **Routing per Order-Typ auch im Transfer.** Nicht möglich: der Transfer-Request trägt keinen
  Order-Typ. Eine Phase-plus-Store-Heuristik ist die einzige zuverlässige Unterscheidung.
- **Fester Platzhalter statt Provider.** Verworfen: der Issue-Punkt „Datenbereitstellung serverseitig"
  verlangt eine echte, seedbare Quelle; ein fester Echo-Payload wäre weder realistisch noch für die
  `090005`-/Verbrauchspfade testbar.
- **Zustandslose Quittung** (nur Returncode, Datenzustand unberührt). Verworfen: positive/negative
  Quittung unterschieden sich dann nur im Code, nicht in der Wirkung — die Verbrauchssemantik verknüpft
  Receipt und Datenbereitstellung realistisch.
