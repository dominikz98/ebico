# 0013 — Upload-Transaktions-Engine & -Speicher

- Status: accepted
- Datum: 2026-07-13

## Kontext

Bis M4 verarbeitete der Server EBICS-Requests **single-shot**: die
[`EbicsRequestPipeline`](../server/host.md) löst genau **einen** Handler pro Request über den
`(Version, OrderType)`-Resolver auf. Der EBICS-**Upload** (Issue #32) ist dagegen die erste
**mehrphasige** Transaktion: eine **Initialisation** (Transaction-ID-Vergabe, Zustandsaufbau) und
eine Folge von **Transfer**-Nachrichten (segmentweise Order-Data). Das kollidiert mit dem
bestehenden Dispatch, weil ein Transfer-Request **keinen** Order-Typ trägt — nur die
`TransactionID` — und mehrere Nachrichten **gemeinsamen Zustand** teilen (Transaktionsschlüssel,
Segmentpuffer, Teilnehmerbindung, Phase).

Zu entscheiden war: (a) wie die Transaktionsphasen an die Pipeline andocken, und (b) wo/wie der
transaktionsübergreifende Zustand gehalten wird.

## Entscheidung

1. **Dedizierte Engine für beide Phasen** statt Aufteilung auf Resolver-Handler (Init) und einen
   separaten Transfer-Pfad: `IUploadTransactionEngine`/`UploadTransactionEngine` besitzt **Init und
   Transfer** und kapselt die Zustandsmaschine. Sie hat einen eigenen Ergebnistyp
   (`UploadTransactionResult`), sodass der Handler-Vertrag (`EbicsOrderResult`) und die Single-Shot-
   Handler (INI/HIA/HPB/HCA/HCS/SPR/HSA) unangetastet bleiben.
2. **Phasen-Routing in der Pipeline vor dem Resolver:** ein signierter `ebicsRequest` mit
   `TransactionID` (bzw. `phase=Transfer`) geht an `ContinueUploadAsync`; ein `ebicsRequest` mit
   `phase=Initialisation` und Order-Typ **FUL** (H003/H004) bzw. **BTU** (H005) an
   `BeginUploadAsync`. Alles andere fällt unverändert auf den `(Version, OrderType)`-Resolver
   zurück — HCA/HCS/SPR (ebenfalls signierte `ebicsRequest`) bleiben Single-Shot.
3. **In-Memory-Transaktionsspeicher** `IUploadTransactionStore` (Default
   `InMemoryUploadTransactionStore`), analog zum Stammdaten-Store aus
   [ADR-0011](0011-server-stammdatenverwaltung.md): thread-sicher, pluggbar via `TryAddSingleton`,
   **keyed auf `Convert.ToHexString(TransactionID)`** (ein `byte[]` taugt nicht als Dictionary-Key).
4. **Transaktions-/Segment-Fehler als Kontrollfluss** (direkt als Returncode zurückgegeben), nicht
   als Exceptions; nur die Dekodier-Fehler (Entschlüsselung/Dekompression) laufen über
   `OrderDataFault` → `EbicsErrorMapper` (`090004`). Es waren **keine** neuen Returncodes nötig — der
   Katalog aus [ADR-0012](0012-returncode-katalog.md) enthält sie bereits.

## Konsequenzen

- Der Resolver-Dispatch bleibt einfach und für die Key-Management-Handler unverändert; die
  Transaktionslogik ist an **einer** Stelle gebündelt (hohe Kohäsion, kein impliziter geteilter
  Zustand über zwei Klassen).
- Neue Order-Typen mit Upload-Semantik können später an dieselbe Engine angebunden werden
  (`IsUploadOrderType`), ohne die Pipeline erneut anzufassen.
- Der In-Memory-Store hält verwaiste (nach Init abgebrochene) und abgeschlossene Transaktionen bis
  zum Neustart — **Eviction/TTL/Recovery ist Issue #35**. Für den Emulator akzeptabel.
- Die **ES-Verifikation** ist bewusst zurückgestellt (Order-Data entschlüsselt, nicht
  authentifiziert) — konsistent mit HCA/HCS; Details und Folge-Arbeit in
  [docs/server/upload-transaction.md](../server/upload-transaction.md).

## Alternativen

- **Init über den Resolver, Transfer über einen Sonderpfad.** Verworfen: der geteilte
  Transaktionszustand hätte zwei Klassen implizit gekoppelt, und `EbicsOrderResult` hätte um
  Transaktions-Felder erweitert werden müssen.
- **Generischer Interceptor für jeden Upload-`ebicsRequest`** (statt fester FUL/BTU-Bindung).
  Verworfen: müsste eine Init von den einphasigen HCA/HCS/SPR unterscheiden — fehleranfällig; die
  Order-Typ-Whitelist ist eindeutig.
