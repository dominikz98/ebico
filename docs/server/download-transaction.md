# Server: Download-Transaktion (Initialisation + Transfer + Receipt)

> Umsetzung von **Issue #33** (Milestone M4 — Server: Transaction Engine). Diese Seite
> beschreibt die serverseitige **Sendemaschine** für einen EBICS-Download: die
> dreiphasige Transaktion aus **Initialisation** (Datenbereitstellung, Komprimieren,
> E002-Verschlüsseln, Segmentieren, Transaktions-ID-Vergabe, erstes Segment),
> **Transfer** (Ausliefern der restlichen Segmente) und **Receipt** (Auswertung der
> Client-Quittung).
>
> Bewusst **enthalten**: die Transaktions-Zustandsmaschine (`DownloadTransactionEngine`), der
> In-Memory-Transaktionsspeicher (`IDownloadTransactionStore`), die serverseitige
> **Datenbereitstellung** (`IDownloadDataProvider` + Admin-API), das Komprimieren
> (`EbicsCompression`)/E002-Verschlüsseln (`EncryptionE002`)/Segmentieren
> (`EbicsSegmentation.Split`) der Order-Data, das Phasen-Routing in der Pipeline (inkl.
> Upload/Download-Unterscheidung), die **Receipt**-Verarbeitung (positiv/negativ) und das
> Auslösen der Transaktions-/Quittungs-Returncodes. Angebunden an die generischen
> Download-OrderTypes **FDL** (H003/H004) und **BTD** (H005).
> Bewusst **noch nicht**: die **Signatur** der Antwort (X002 = M4; die Antwort ist unsigniert);
> **Recovery/Timeouts** und die Eviction verwaister Transaktionen (#35); ein echter
> persistenter Auftragsdatenspeicher (der `IDownloadDataProvider` ist In-Memory, austauschbar);
> auftragstypspezifische Datengenerierung (FDL-/BTD-Parameter werden nicht ausgewertet).

## Zweck

Ein EBICS-Download überträgt Auftragsdaten in **drei Phasen**. In der **Initialisation** fordert
der Client Daten an (Order-Typ, Teilnehmer); der Server **stellt die Daten bereit**, komprimiert
und E002-verschlüsselt sie für den **Verschlüsselungsschlüssel des Teilnehmers** (wie bei HPB),
**segmentiert** den Chiffretext, vergibt eine **Transaction-ID** und liefert `NumSegments` +
Segment 1 zurück. In der **Transfer**-Phase holt der Client die Segmente 2…N ab. In der
**Receipt**-Phase quittiert der Client den Empfang (`ReceiptCode` 0 = positiv, 1 = negativ); der
Server schließt die Nachbearbeitung ab.

#33 spiegelt die [Upload-Transaktion](upload-transaction.md) (#32) in die Gegenrichtung: sie
komponiert dieselben policy-freien Primitiven ([Segmentierung](segmentation.md),
[E002](../protocol/encryption-e002.md), [Kompression](segmentation.md)) — hier in **Senderichtung**
(`Split`/`Compress`/`Encrypt` statt `Reassemble`/`Decrypt`/`Decompress`) — zu einer
Zustandsmaschine. Der Server→Client-Nutzdatenpfad ist derselbe wie bei
[HPB](hpb.md), nur mehrsegmentig und in eine Transaktion eingebettet.

## Datenbereitstellung serverseitig

Woher die Download-Daten kommen, kapselt `IDownloadDataProvider` (`EBICO.Server.State`). Der Default
`InMemoryDownloadDataProvider` hält je **(Teilnehmer, Order-Typ)** eine **FIFO-Queue** von
Klartext-Order-Data. Eine Download-Initialisation **entnimmt** (`TryDequeueAsync`) das nächste
Element; ist die Queue leer, antwortet die Engine mit `090005` (`EBICS_NO_DOWNLOAD_DATA_AVAILABLE`).

**Verbrauchssemantik:** die Initialisation entnimmt die Daten sofort. Eine **positive** Quittung
(`011000`) lässt sie entnommen (verbraucht) — ein erneuter Download liefert `090005`. Eine
**negative** Quittung (`011001`) **stellt die Daten wieder ein** (`EnqueueAsync`), sodass sie erneut
abgerufen werden können.

Daten werden über die **Admin-API** eingestellt (unauthentifiziert, nur lokal/Emulator, wie die
[Stammdaten-Admin-API](master-data.md)):

| Methode | Pfad | Wirkung |
| --- | --- | --- |
| `POST` | `…/subscribers/{userId}/downloads/{orderType}` | Body `{"base64Data":"…"}` → Order-Data (base64) einreihen; Antwort `{"pending":n}` |
| `GET` | `…/subscribers/{userId}/downloads/{orderType}` | Anzahl wartender Payloads: `{"pending":n}` |

(voller Pfad: `/admin/banks/{hostId}/partners/{partnerId}/subscribers/{userId}/downloads/{orderType}`;
ungültiges Base64 → HTTP 400). Ein echter Auftragsdatenspeicher kann via `TryAddSingleton` vor
`AddEbicoServer` untergeschoben werden.

## Ablauf

Der Server routet die Phase in der Pipeline **vor** dem Resolver: `phase=Receipt` ist Download-only;
ein `ebicsRequest` mit `TransactionID` (bzw. `phase=Transfer`) geht an die Download-Engine, **wenn**
die ID einer Download-Transaktion gehört (`OwnsTransaction`), sonst an die Upload-Engine; ein
`ebicsRequest` mit `phase=Initialisation` und Order-Typ **FDL/BTD** startet einen Download.

### Phase 1 — Initialisation

| Schritt | Aktion |
| --- | --- |
| 1. Identität | `HostID`/`PartnerID`/`UserID` prüfen; Teilnehmer muss existieren und `Ready` sein (sonst `091002`) |
| 2. Enc-Schlüssel | Verschlüsselungsschlüssel (`E00x`) des Teilnehmers aus `IServerKeyStore` (fehlt → `091002`) |
| 3. Datenbereitstellung | nächste Order-Data via `IDownloadDataProvider.TryDequeueAsync` (leer → `090005`, keine Transaktion) |
| 4. Aufbereiten | `EbicsCompression.Compress` → `EncryptionE002.Encrypt` (für den Teilnehmer-Enc-Key) → `PublicKeyFingerprint.Compute` |
| 5. Segmentieren | `EbicsSegmentation.Split(ciphertext, SegmentSizeBytes)`; `NumSegments > MaxDownloadSegments` → `091114` (Daten werden zurückgestellt) |
| 6. Transaktion anlegen | 16-Byte-`TransactionID` erzeugen, Zustand (Subscriber, OrderType, Segmente, Enc-Info, Klartext für Re-Enqueue) im `IDownloadTransactionStore` |
| 7. Antwort | `ebicsResponse`, `phase=Initialisation`, `TransactionID`, `NumSegments`, `SegmentNumber=1`, `DataTransfer` (DataEncryptionInfo + Segment 1), `EBICS_OK` |

```xml
<!-- Request (gekürzt) -->
<ebicsRequest Version="H004" ...>
  <header authenticate="true">
    <static>
      <HostID>EBICOHOST</HostID> <PartnerID>PARTNER01</PartnerID> <UserID>USER01</UserID>
      <OrderDetails><OrderType>FDL</OrderType> ... </OrderDetails>
    </static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase></mutable>
  </header>
  <body/>
</ebicsRequest>

<!-- Response (gekürzt) -->
<ebicsResponse Version="H004" ...>
  <header><static><TransactionID>…</TransactionID><NumSegments>3</NumSegments></static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase>
      <SegmentNumber lastSegment="false">1</SegmentNumber>
      <ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body><DataTransfer>
    <DataEncryptionInfo authenticate="true">
      <EncryptionPubKeyDigest Version="E002" Algorithm="…sha256">…</EncryptionPubKeyDigest>
      <TransactionKey>…</TransactionKey>              <!-- RSA-OAEP für den Teilnehmer -->
    </DataEncryptionInfo>
    <OrderData>…segment 1…</OrderData>
  </DataTransfer><ReturnCode>000000</ReturnCode></body>
</ebicsResponse>
```

### Phase 2 — Transfer (Segmente 2…N)

| Schritt | Aktion |
| --- | --- |
| 1. Transaktion finden | `Static/TransactionID` → Hex-Lookup im Store (fehlt → `091101`) |
| 2. Segmentnummer prüfen | `Mutable/SegmentNumber` in `[1, NumSegments]` (0/fehlt → `091112`, > N → `091104`) |
| 3. Segment liefern | Segment k aus dem Zustand; `lastSegment` serverseitig aus `k == NumSegments` |
| 4. Antwort | `ebicsResponse`, `phase=Transfer`, `TransactionID`, `SegmentNumber`, `DataTransfer/OrderData` (**kein** DataEncryptionInfo, **kein** NumSegments), `EBICS_OK` |

Das `DataEncryptionInfo` (Transaktionsschlüssel + Digest) reist **nur** in der Init-Antwort; die
Transfer-Antworten tragen ausschließlich das jeweilige `OrderData`-Segment. Der Client reassembliert
alle Segmente (Init-Segment 1 + Transfer 2…N), entschlüsselt mit dem einmalig gelieferten
Transaktionsschlüssel (RSA-OAEP mit seinem privaten Enc-Key) und dekomprimiert.

### Phase 3 — Receipt (Quittung)

| Schritt | Aktion |
| --- | --- |
| 1. Transaktion finden | `Static/TransactionID` → Hex-Lookup (fehlt → `091101`) |
| 2. Quittung lesen | `body/TransferReceipt/ReceiptCode` (fehlt → `091112`) |
| 3. Nachbearbeitung | Transaktion entfernen; `ReceiptCode=0` → Daten bleiben verbraucht; sonst Daten via Provider wieder einreihen |
| 4. Antwort | `ebicsResponse`, `phase=Receipt`, `TransactionID`, `011000` (positiv) bzw. `011001` (negativ) |

```xml
<!-- Receipt-Request (gekürzt) -->
<ebicsRequest Version="H004" ...>
  <header authenticate="true">
    <static><HostID>EBICOHOST</HostID><TransactionID>…</TransactionID></static>
    <mutable><TransactionPhase>Receipt</TransactionPhase></mutable>
  </header>
  <body><TransferReceipt authenticate="true"><ReceiptCode>0</ReceiptCode></TransferReceipt></body>
</ebicsRequest>
```

## Returncodes & Fehlerfälle

| Situation | Phase | Returncode | Ablage |
| --- | --- | --- | --- |
| Erfolg (Init/Transfer) | Init/Transfer | `000000` EBICS_OK | Header + Body |
| positive Quittung | Receipt | `011000` EBICS_DOWNLOAD_POSTPROCESS_DONE | Header |
| negative Quittung | Receipt | `011001` EBICS_DOWNLOAD_POSTPROCESS_SKIPPED | Header |
| Teilnehmer unbekannt/nicht `Ready`/kein Enc-Key | Init | `091002` EBICS_INVALID_USER_OR_USER_STATE | Body |
| keine Download-Daten vorhanden | Init | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | Body |
| `NumSegments` > `MaxDownloadSegments` | Init | `091114` EBICS_MAX_SEGMENTS_EXCEEDED | Body |
| unbekannte/entfernte `TransactionID` | Transfer/Receipt | `091101` EBICS_TX_UNKNOWN_TXID | Body |
| `SegmentNumber` fehlt / 0 bzw. Receipt ohne `ReceiptCode` | Transfer/Receipt | `091112` EBICS_INVALID_REQUEST_CONTENT | Body |
| `SegmentNumber` > `NumSegments` | Transfer | `091104` EBICS_TX_SEGMENT_NUMBER_EXCEEDED | Body |

Die Header-/Body-Ablage folgt automatisch aus `EbicsReturnCode.Kind` (`EbicsResponseFactory.Split`):
`011000`/`011001` sind **technisch** → Header, die übrigen **fachlich** → Body. Es waren **keine**
neuen Returncodes nötig — alle liegen bereits im [Katalog](../protocol/return-codes.md). Alle Fälle
werden mit **HTTP 200** und dem Returncode im `ebicsResponse` beantwortet (siehe
[Grundregel](host.md)).

### ⚠️ Spec-Vorbehalte

- **Phasen-/Feld-Verteilung.** Dass `NumSegments` + Segment 1 in der Init-Antwort reisen, die
  Segmente 2…N im Transfer, und `DataEncryptionInfo` **nur** in der Init-Antwort, ist die kanonische
  Lesart und **gegen den offiziellen EBICS-Annex zu verifizieren**. Ebenso das `SegmentNumber=1`-Echo
  in der Init-Antwort.
- **ReceiptCode-Semantik.** `0 = positiv → 011000`, `≠0 = negativ → 011001` ist die angenommene
  Zuordnung; gegen den Annex zu verifizieren.
- **Unsignierte Antwort.** Die Antwort ist **nicht** signiert (X002 = M4); es wird keine
  Order-Signatur erzeugt. Vertraulichkeit besteht dennoch (Order-Data für den Teilnehmer-Enc-Key
  verschlüsselt).
- **Segmentgröße roh vs. base64.** `SegmentSizeBytes` misst Roh-Bytes; der Bezug der EBICS-Segment-
  grenze (roh vs. base64) ist offen (siehe [Segmentierung](segmentation.md)).
- **Verwaiste Transaktionen.** Der Idle-Timeout und die Eviction (lazy beim Zugriff + Hintergrund-
  Sweeper) sind in **[#35](transaction-recovery.md)** umgesetzt: bricht der Client nach Init/Transfer
  ohne Receipt ab, läuft die Transaktion ab und wird entfernt — die bereits entnommenen Daten werden
  dabei **wieder eingereiht** (wie bei einer negativen Quittung), gehen also nicht verloren.

## EBICS-Versionsbezug

Die Byte-Pipeline ist versionsagnostisch; nur die Envelope-/Header-Details unterscheiden sich:

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Download-Order-Typ | `OrderDetails/OrderType` = **FDL** | `OrderDetails/AdminOrderType` = **BTD** |
| Order-Parameter | `FDLOrderParams` | `BTDOrderParams` (BTF) |
| Transaktions-Header | `TransactionID`/`NumSegments`/`SegmentNumber`+`lastSegment` — strukturgleich | dito |
| Response-`DataTransfer` | `DataEncryptionInfo` + `OrderData` — strukturgleich | dito |

Genau **ein** `OrderData`-Element pro Transfer-Nachricht (Binding). Die `TransactionID` ist 16 Byte
(`hexBinary`); intern Store-Key als Hex-String.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Request-XML aus committeten Core-Bindings,
keine proprietären Fixtures):

- `DownloadTransactionTests` (`[Theory]` über H003/H004/H005) — **Happy Path 1 Segment** (Init →
  `TransactionID` + `NumSegments=1` + `DataTransfer`; das gelieferte Segment wird mit dem **privaten**
  Teilnehmer-Enc-Key entschlüsselt und dekomprimiert == Original; Receipt(0) → `011000`, Store leer)
  und **N Segmente** (kleine `SegmentSizeBytes` erzwingt mehrere Segmente; alle reassembliert +
  entschlüsselt == Original; nur die Init trägt `DataEncryptionInfo`). Negativfälle: keine Daten
  (`090005`), Teilnehmer nicht `Ready` (`091002`), unbekannte `TransactionID` (`091101`),
  `SegmentNumber` > N (`091104`) bzw. 0 (`091112`), Receipt unbekannte TxID (`091101`).
  **Verbrauch:** nach Receipt(0) → erneuter Download `090005`; nach Receipt(1) → Daten wieder
  verfügbar. **Routing-Regression:** parallele Upload- + Download-TxID landen je bei der richtigen
  Engine (`OwnsTransaction`-Disambiguierung).
- `DownloadTransactionStoreTests` — `InMemoryDownloadTransactionStore` (Create/TryGet/Remove/Count,
  Hex-Keying, Duplikat-Create, `GetSegment`-Grenzen) und `InMemoryDownloadDataProvider`
  (Enqueue/Dequeue-FIFO, Count, leere Queue, Isolation je (Teilnehmer, Order-Typ)).
- `EbicsEndpointIntegrationTests` — Download über den HTTP-Endpoint (`WebApplicationFactory`):
  Order-Data via **Admin-API**-`POST` einstellen → `POST /ebics` Init → Entschlüsselung == Original →
  Receipt(0) → `011000`; die Admin-Queue ist danach leer (Verbrauch).

## Verwandte Doku

- [Upload-Transaktion (Initialisation + Transfer)](upload-transaction.md) — das gespiegelte Pendant (Empfangsrichtung)
- [Segmentierung, Kompression & Base64-Pipeline](segmentation.md) — die genutzten Byte-Primitiven (Senderichtung: `Split`)
- [Verschlüsselung E002](../protocol/encryption-e002.md) — hybride Verschlüsselung für den Teilnehmer-Enc-Key
- [HPB — Abruf der Bankschlüssel](hpb.md) — Vorbild für den verschlüsselten Server→Client-Nutzdatenfluss (einsegmentig)
- [Stammdatenverwaltung & Admin-API](master-data.md) — Muster der unauthentifizierten Admin-API
- [Hostable Server-Grundgerüst](host.md) — Pipeline, Fehlerabbildung, `EbicoServerOptions`
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — die ausgelösten Transaktions-/Quittungs-Codes
- [ADR-0014 (Download-Transaktions-Engine)](../adr/0014-download-transaktions-engine.md) — dedizierte Engine, Routing-Disambiguierung, Provider & Verbrauchssemantik
