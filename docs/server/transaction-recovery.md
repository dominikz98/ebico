# Server: Transaktions-Recovery & Timeouts

> Umsetzung von **Issue #35** (Milestone M4 — Server: Transaction Engine). Diese Seite
> beschreibt, wie die Transaction Engine unterbrochene Transaktionen behandelt: den **Ablauf
> (Timeout) von Transaction-IDs**, die **Eviction** verwaister/abgeschlossener Transaktionen
> (lazy beim Zugriff **und** über einen Hintergrunddienst) und die **Idempotenz** bei
> wiederholten Nachrichten.
>
> Bewusst **enthalten**: der gleitende Idle-Timeout je Transaktion (`LastActivityAt`/`Touch`/
> `IsExpired`), die Lazy-Expiry im Transfer, der `TransactionCleanupService` (BackgroundService)
> samt `ITransactionEvictor`, das Re-Enqueuen der entnommenen Download-Daten bei Ablauf, die
> obere Schranke paralleler Transaktionen (`091115`) und die definierte Idempotenz für doppelte
> Segmente/Init/Receipt.
> Bewusst **noch nicht**: ein **aktiver Recovery-Sync-Flow** (Client-Recovery-Flag in der
> Initialisation, Server antwortet mit Resume-Segment) — der genaue EBICS-Recovery-Ablauf hängt
> an den proprietären XSDs/Annexen und ist gegen die Spec zu verifizieren; `061101`
> `EBICS_TX_RECOVERY_SYNC` ist im [Katalog](../protocol/return-codes.md) vorhanden, wird aber
> (noch) **nicht** aktiv ausgelöst.

## Zweck

Eine EBICS-Transaktion erstreckt sich über **mehrere** Nachrichten (Initialisation → Transfer …,
beim Download plus Receipt). Bricht ein Client dazwischen ab — verlorene Verbindung, kein Receipt —,
bleibt serverseitig Zustand liegen: beim Upload der Segmentpuffer und der Transaktionsschlüssel, beim
Download die bereits **entnommenen** Order-Data „in Bearbeitung". Bis #35 wurde dieser Zustand **nie**
aufgeräumt (die Upload-Engine rief nie `Remove`, die Download-Engine nur beim Receipt) — der In-Memory-
Store wuchs unbegrenzt (siehe [ADR-0013](../adr/0013-upload-transaktions-engine.md)/
[ADR-0014](../adr/0014-download-transaktions-engine.md)).

#35 gibt jeder Transaktion eine **begrenzte Lebensdauer** und räumt abgelaufene Transaktionen ab.
Dieselbe Retention ist zugleich das **Idempotenz-/Replay-Fenster**: solange eine (auch abgeschlossene)
Transaktion nicht abgelaufen ist, bleibt sie erkennbar, sodass Wiederholungen sauber beantwortet werden.

## Idle-Timeout (gleitendes Fenster)

Jede Transaktion trägt neben `CreatedAt` einen `LastActivityAt`-Zeitstempel. Er wird bei der Anlage auf
`CreatedAt` gesetzt und bei **jedem** akzeptierten Transfer-Schritt via `Touch(now)` auf „jetzt"
verschoben (lock-frei über `Interlocked`). `IsExpired(now, timeout)` ist `true`, sobald die Transaktion
mindestens `timeout` **untätig** war:

```csharp
public bool IsExpired(DateTimeOffset now, TimeSpan timeout)
    => timeout > TimeSpan.Zero && now.UtcTicks - LastActivityAt.UtcTicks >= timeout.Ticks;
```

Das Fenster ist **gleitend** (Aktivität, nicht Erstellungszeit) — ein langer, aber laufender
Multi-Segment-Transfer läuft nicht mitten drin ab. Ein `timeout` ≤ `TimeSpan.Zero` **deaktiviert** den
Ablauf vollständig.

Konfiguration in [`EbicoServerOptions`](host.md):

| Option | Default | Wirkung |
| --- | --- | --- |
| `TransactionTimeout` | `1h` | Idle-Timeout je Transaktion; `≤ 0` = deaktiviert |
| `TransactionCleanupInterval` | `1min` | Sweep-Intervall des Hintergrunddienstes; `≤ 0` = Sweeper aus |
| `MaxConcurrentTransactions` | `0` | obere Schranke paralleler Transaktionen je Store; `0` = unbegrenzt |

## Eviction: lazy + Hintergrund-Sweeper

**Lazy (beim Zugriff).** Findet ein Transfer die Transaktion, wird sie **vor** der weiteren
Verarbeitung auf Ablauf geprüft. Ist sie abgelaufen, wird sie entfernt und wie eine unbekannte ID
beantwortet — `091101` `EBICS_TX_UNKNOWN_TXID`. Andernfalls wird `Touch(now)` gesetzt und normal
weiterverarbeitet.

**Hintergrund-Sweeper.** `TransactionCleanupService` (`BackgroundService`) sweept alle
`TransactionCleanupInterval` über die registrierten `ITransactionEvictor` (beide Engines) und entfernt
abgelaufene Transaktionen — auch solche, die der Client **nie wieder anfasst** (die die Lazy-Expiry also
nie sähe). Das begrenzt den Speicher unabhängig vom Client-Verhalten. Der Sweeper ist robust: bei
deaktiviertem Intervall startet er gar keinen Timer, und ein Fehler eines einzelnen Sweeps wird geloggt,
ohne die Schleife oder den Host abzureißen.

```csharp
public interface ITransactionEvictor
{
    Task<int> EvictExpiredAsync(CancellationToken ct = default); // entfernt abgelaufene, liefert Anzahl
}
```

Beide Engines implementieren `ITransactionEvictor`; die Registrierung in `AddEbicoServer` leitet die
vorhandenen Engine-Singletons zusätzlich als `ITransactionEvictor` weiter (dieselben Instanzen) und fügt
`AddHostedService<TransactionCleanupService>()` hinzu.

### Download: Re-Enqueue bei Ablauf

Ein Download entnimmt die Order-Data schon in der Initialisation (Verbrauchssemantik, siehe
[Download-Transaktion](download-transaction.md)). Läuft die Transaktion ab (lazy oder per Sweeper),
werden die Daten **wieder eingereiht** (`IDownloadDataProvider.EnqueueAsync`) — analog zur negativen
Quittung —, damit sie nicht verloren gehen. Der `Remove`-Rückgabewert dient als **„genau einmal"-Guard**
gegen das Rennen Lazy-Pfad ↔ Sweeper: wer die Transaktion tatsächlich entfernt, reiht wieder ein; der
Verlierer tut nichts. So landen die Daten exakt einmal zurück in der Queue.

Der **Receipt** prüft bewusst **keinen** Ablauf: liegt die Transaktion beim Receipt noch vor, hat der
Client die Daten tatsächlich empfangen und quittiert — die Quittung zu ehren ist korrekter, als sie
fälschlich zu verwerfen (und bei positiver Quittung die Daten neu einzureihen). Ist die Transaktion
bereits evinct, greift der normale `TryGet`-Fehlpfad → `091101`.

## Idempotenz / doppelte Segmente

Die Retention macht Wiederholungen erkennbar; das Verhalten ist damit definiert:

| Wiederholung | Antwort | Bemerkung |
| --- | --- | --- |
| doppeltes **Transfer-Segment** (Upload, innerhalb Retention) | `091103` `EBICS_TX_MESSAGE_REPLAY` | bestehende Segment-Duplikaterkennung (#32) |
| **Transfer** gegen abgelaufene/entfernte Transaktion | `091101` `EBICS_TX_UNKNOWN_TXID` | Retention-Fenster überschritten |
| wiederholte **Initialisation** | neue Transaktion (neue Zufalls-ID) | EBICS kennt keinen Client-Idempotency-Key in der Init; ein Download entnimmt dabei erneut |
| wiederholter **Receipt** nach Abschluss (Download) | `091101` | die erste Quittung hat die Transaktion entfernt |

## Concurrent-Transaction-Schranke (091115)

Ist `MaxConcurrentTransactions > 0`, wird eine Initialisation abgewiesen (`091115`
`EBICS_MAX_TRANSACTIONS_EXCEEDED`), sobald der jeweilige Store die Schranke erreicht — beim Download
**vor** dem Entnehmen der Daten (eine abgelehnte Init darf keine Daten konsumieren). Die Prüfung ist ein
**weiches** Limit (Count-dann-Create ist nicht atomar) und zählt abgeschlossene, noch nicht evinct­e
Transaktionen im Retention-Fenster mit. Für den Emulator bewusst akzeptiert.

## Returncodes

Es waren **keine** neuen Returncodes nötig — alle liegen bereits im
[Katalog](../protocol/return-codes.md):

| Situation | Returncode | Ablage |
| --- | --- | --- |
| abgelaufene/entfernte `TransactionID` (Transfer/Receipt) | `091101` EBICS_TX_UNKNOWN_TXID | Body |
| doppeltes Upload-Segment (Replay) | `091103` EBICS_TX_MESSAGE_REPLAY | Body |
| zu viele parallele Transaktionen | `091115` EBICS_MAX_TRANSACTIONS_EXCEEDED | Body |
| (verfügbar, nicht ausgelöst) Recovery-Resync | `061101` EBICS_TX_RECOVERY_SYNC | Header |

### ⚠️ Spec-Vorbehalte

- **Kein aktiver Recovery-Sync-Flow.** Der Zustandserhalt (Retention) ist die Voraussetzung für Recovery;
  ein spec-genauer client-getriebener Recovery-Ablauf (Recovery-Flag, Resume-Segmentnummer, `061101`)
  ist gegen die offiziellen EBICS-Annexe zu verifizieren und **zurückgestellt**.
- **Timeout-Wert.** EBICS schreibt keinen festen Transaktions-Timeout vor; der Default (1 h) ist
  emulator-pragmatisch und konfigurierbar.
- **Receipt ignoriert Ablauf** bewusst (siehe oben) — die genaue Bank-Policy für „Receipt nach Timeout"
  ist gegen die Spec zu verifizieren.
- **Weiches Concurrent-Limit** (nicht atomar) und Retention-Zählung sind bewusste Emulator-Kompromisse.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Zeit über den `MutableTimeProvider` aus
`tests/EBICO.Tests/Connector/TestDoubles.cs`, der die Uhr vorrücken kann):

- `TransactionRecoveryTests` — end-to-end über die Pipeline: Upload/Download-Transfer nach Timeout →
  `091101` (Upload entfernt, Download re-enqueued); gleitendes Fenster (aktiver Multi-Segment-Transfer
  läuft trotz Gesamtdauer > Timeout nicht ab); Replay innerhalb der Retention (`091103`) vs. nach Ablauf
  (`091101`); `MaxConcurrentTransactions` (`091115`); direkte `EvictExpiredAsync`-Prüfung (entfernt
  abgelaufene, hält aktive; deaktiviert = No-Op; Re-Enqueue genau einmal); Receipt nach Timeout wird
  geehrt; wiederholter Receipt → `091101`.
- `UploadTransactionStoreTests` / `DownloadTransactionStoreTests` — `GetAll()`-Snapshot (entkoppelt von
  späterem `Remove`) und die Objekt-Semantik `LastActivityAt`/`Touch`/`IsExpired` (inkl. deaktiviertem
  Timeout).
- `TransactionCleanupServiceTests` — deaktiviertes Intervall schließt sofort ab; null-Guards.
- `EbicoServerServiceCollectionExtensionsTests` — Default-Optionen, Registrierung des Hosted Service und
  beider Engines als `ITransactionEvictor` (dieselben Instanzen).

## Verwandte Doku

- [Upload-Transaktion (Initialisation + Transfer)](upload-transaction.md) — der zweiphasige Upload
- [Download-Transaktion (Initialisation + Transfer + Receipt)](download-transaction.md) — der dreiphasige Download inkl. Verbrauchssemantik
- [Hostable Server-Grundgerüst](host.md) — Pipeline, `EbicoServerOptions`, DI
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — die genutzten Transaktions-/Segment-Codes
- [ADR-0013](../adr/0013-upload-transaktions-engine.md) / [ADR-0014](../adr/0014-download-transaktions-engine.md) — die Engines, die #35 um Eviction/TTL ergänzt
