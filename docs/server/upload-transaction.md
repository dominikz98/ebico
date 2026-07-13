# Server: Upload-Transaktion (Initialisation + Transfer)

> Umsetzung von **Issue #32** (Milestone M4 — Server: Transaction Engine). Diese Seite
> beschreibt die serverseitige **Empfangsmaschine** für einen EBICS-Upload: die
> zweiphasige Transaktion aus **Initialisation** (Transaktions-ID-Vergabe, Zustandsaufbau)
> und **Transfer** (segmentweiser Empfang, Reassemblierung, Entschlüsselung, Dekompression).
>
> Bewusst **enthalten**: die Transaktions-Zustandsmaschine (`UploadTransactionEngine`), der
> In-Memory-Transaktionsspeicher (`IUploadTransactionStore`), die 16-Byte-Transaktions-ID,
> das Phasen-Routing in der Pipeline, das Puffern/Reassemblieren der Segmente
> (`EbicsSegmentation.Reassemble`), Entschlüsselung (`EncryptionE002`) und Dekompression
> (`EbicsCompression`) der reassemblierten Order-Data, sowie das Auslösen der
> Transaktions-/Segment-Returncodes. Angebunden an die generischen Upload-OrderTypes
> **FUL** (H003/H004) und **BTU** (H005).
> Bewusst **noch nicht**: die **Signaturprüfung des OrderData** (ES / A00x) — die
> `SignatureData` aus der Initialisation wird **einbehalten**, aber **nicht** kryptografisch
> verifiziert (siehe Spec-Vorbehalte, Folge-Issue); die **Download**-Transaktion inkl.
> Receipt-Phase (#33); **Recovery/Timeouts** und die Eviction verwaister Transaktionen (#35);
> die X002-Request-Signaturprüfung (Verify-Stufe bleibt No-Op).

## Zweck

Ein EBICS-Upload überträgt Auftragsdaten in **zwei Phasen**. In der **Initialisation** kündigt
der Client die Transaktion an (Order-Typ, Anzahl Segmente, verschlüsselter Transaktionsschlüssel,
elektronische Unterschrift); der Server vergibt eine **Transaction-ID** und legt den
Transaktionszustand an. In der **Transfer**-Phase liefert der Client die Order-Data als
`base64(encrypt(compress(orderDataXml)))`, aufgeteilt in **Segmente** — ein
`DataTransfer/OrderData` je Nachricht. Beim letzten Segment reassembliert der Server die Segmente,
entschlüsselt sie mit dem Transaktionsschlüssel und dekomprimiert sie zur Klartext-Order-Data.

#32 komponiert dazu die bereits vorhandenen, policy-freien Primitiven aus #34/M2/M3
([Segmentierung](segmentation.md), [E002](../protocol/encryption-e002.md),
[Kompression](segmentation.md)) zu einer **Zustandsmaschine** und liefert das **Wann/Wer**
(Phasen, Transaction-ID, Envelope, Segment-Policy), das die Primitiven bewusst offen ließen.

## Ablauf

Der Server unterscheidet die Phase am `TransactionPhase`-Feld des `ebicsRequest` (und — robust
gegen ein fehlendes Feld — an der Präsenz einer `TransactionID` im Static-Header). Ein
`ebicsRequest` mit `phase=Initialisation` und Order-Typ **FUL/BTU** startet die Transaktion; jeder
`ebicsRequest` mit `TransactionID` setzt sie fort. Alle übrigen `ebicsRequest`
(HCA/HCS/SPR …) laufen unverändert über den Single-Shot-Handler-Resolver.

### Phase 1 — Initialisation

| Schritt | Aktion |
| --- | --- |
| 1. Identität | `HostID`/`PartnerID`/`UserID` prüfen; Teilnehmer muss existieren und `Ready` sein (sonst `091002`) |
| 2. Segmentzahl | `Static/NumSegments` muss ≥ 1 und ≤ `EbicoServerOptions.MaxUploadSegments` sein (sonst `091114`) |
| 3. Transaktionsschlüssel | `DataTransfer/DataEncryptionInfo/TransactionKey` mit dem **privaten** Bank-Enc-Key entschlüsseln (`EncryptionE002.DecryptTransactionKey`) |
| 4. ES einbehalten | `DataTransfer/SignatureData` roh im Zustand ablegen (Verifikation zurückgestellt) |
| 5. Transaktion anlegen | 16-Byte-`TransactionID` erzeugen, Zustand (Subscriber, OrderType, NumSegments, txKey, ES) im `IUploadTransactionStore` speichern |
| 6. Antwort | `ebicsResponse`, `phase=Initialisation`, `TransactionID`, `EBICS_OK` |

```xml
<!-- Request (gekürzt) -->
<ebicsRequest Version="H004" ...>
  <header authenticate="true">
    <static>
      <HostID>EBICOHOST</HostID> <PartnerID>PARTNER01</PartnerID> <UserID>USER01</UserID>
      <OrderDetails><OrderType>FUL</OrderType> ... </OrderDetails>
      <NumSegments>3</NumSegments>
    </static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase></mutable>
  </header>
  <body><DataTransfer>
    <DataEncryptionInfo authenticate="true"><TransactionKey>…</TransactionKey> …</DataEncryptionInfo>
    <SignatureData authenticate="true">…</SignatureData>   <!-- ES: einbehalten, nicht geprüft -->
  </DataTransfer></body>
</ebicsRequest>

<!-- Response (gekürzt) -->
<ebicsResponse Version="H004" ...>
  <header><static><TransactionID>…</TransactionID></static>
    <mutable><TransactionPhase>Initialisation</TransactionPhase><ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body><ReturnCode>000000</ReturnCode></body>
</ebicsResponse>
```

### Phase 2 — Transfer (je Segment 1…N)

| Schritt | Aktion |
| --- | --- |
| 1. Transaktion finden | `Static/TransactionID` → Hex-Lookup im Store (fehlt → `091101`) |
| 2. Segmentnummer prüfen | `Mutable/SegmentNumber` in `[1, NumSegments]` (0 → `091112`, > N → `091104`) |
| 3. Segment puffern | Order-Data-Bytes unter `SegmentNumber` ablegen; Duplikat → `091103` |
| 4. Vollzähligkeit | bei `lastSegment=true`: alle `NumSegments` da? (sonst `011101`) |
| 5. Dekodieren | `Reassemble` → `EncryptionE002.DecryptOrderData(txKey)` → `EbicsCompression.Decompress` (Fehler → `090004`) |
| 6. Antwort | `ebicsResponse`, `phase=Transfer`, `TransactionID`, `SegmentNumber`, `EBICS_OK` |

`Reassemble` konkateniert die Segmente in **Segmentnummer-Reihenfolge** (`SortedDictionary`), die
Reihenfolge des Eintreffens ist egal. Die reassemblierte, entschlüsselte und dekomprimierte
Klartext-Order-Data wird auf der abgeschlossenen Transaktion (`UploadTransaction.OrderData`)
festgehalten — die auftragstypspezifische Weiterverarbeitung ist Folge-Arbeit.

## Returncodes & Fehlerfälle

| Situation | Returncode | Ablage |
| --- | --- | --- |
| Erfolg (Init/Transfer) | `000000` EBICS_OK | Header + Body |
| Teilnehmer unbekannt / nicht `Ready` | `091002` EBICS_INVALID_USER_OR_USER_STATE | Body |
| `NumSegments` fehlt / 0 bzw. Segmentnummer 0 | `091112` EBICS_INVALID_REQUEST_CONTENT | Body |
| `NumSegments` > `MaxUploadSegments` | `091114` EBICS_MAX_SEGMENTS_EXCEEDED | Body |
| unbekannte / abgelaufene `TransactionID` | `091101` EBICS_TX_UNKNOWN_TXID | Body |
| `SegmentNumber` > `NumSegments` | `091104` EBICS_TX_SEGMENT_NUMBER_EXCEEDED | Body |
| doppeltes Segment (Replay) | `091103` EBICS_TX_MESSAGE_REPLAY | Body |
| `lastSegment` vor Vollzähligkeit | `011101` EBICS_TX_SEGMENT_NUMBER_UNDERRUN | Header |
| Order-Data nicht entschlüsselbar/dekomprimierbar | `090004` EBICS_INVALID_ORDER_DATA_FORMAT | Body |

Die Transaktions-/Segment-Codes sind **Kontrollfluss** und werden von der Engine direkt gesetzt; die
Dekodier-Fehler (Entschlüsselung/Dekompression) laufen über `OrderDataFault` → den bestehenden
`EbicsErrorMapper` (`090004`). Alle Fälle werden mit **HTTP 200** und dem Returncode im
`ebicsResponse` beantwortet (siehe [Grundregel im Host-Grundgerüst](host.md)).

### ⚠️ Spec-Vorbehalte

- **ES-Verifikation zurückgestellt.** Die elektronische Unterschrift (`SignatureData`, A005/A006)
  wird eingelesen und im Transaktionszustand einbehalten, aber **nicht** geprüft — konsistent mit den
  einphasigen Key-Handlern (HCA/HCS). Die Order-Data ist damit entschlüsselt, aber nicht
  authentifiziert. Nachziehen via `BankSignature.Verify` in einem Folge-Issue.
- **Init/Transfer-Aufteilung.** Dass die `SignatureData`/`DataEncryptionInfo` in der Initialisation
  und die `OrderData`-Segmente ausschließlich im Transfer reisen (kein Segment in der Init), ist die
  kanonische Lesart und **gegen den offiziellen EBICS-Annex zu verifizieren**.
- **BTF `SignatureFlag` (H005).** Ob für einen konkreten BTU-Auftrag überhaupt eine ES gefordert ist,
  steuert spec-seitig `BTUOrderParams/SignatureFlag`; #32 wertet das noch nicht aus.
- **S001 `OrderSignature` vs. `OrderSignatureData`.** Für die spätere ES-Prüfung ist festzulegen,
  welchen der beiden S001-Träger der Sender befüllt (S002 kennt nur `OrderSignatureData`).
- **Segmentgröße roh vs. base64.** `SegmentSizeBytes` misst Roh-Bytes; der Bezug der EBICS-Segment-
  grenze (roh vs. base64) ist offen (siehe [Segmentierung](segmentation.md)).
- **Response-Felder.** `NumSegments` wird in der Upload-Antwort nicht gesetzt (laut Schema
  Download-only); ob der Transfer-Response `SegmentNumber` echoen muss, ist zu verifizieren (wird
  gesetzt, wenn vorhanden). Die Antwort ist weiterhin **unsigniert** (X002 = M4).
- **Verwaiste Transaktionen.** Bricht der Client nach der Initialisation ab, bleibt der Zustand im
  In-Memory-Store liegen (kein TTL/keine Eviction) — Recovery/Timeouts sind **#35**.

## EBICS-Versionsbezug

Die Byte-Pipeline ist versionsagnostisch; nur die Envelope-/Header-Details unterscheiden sich:

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Upload-Order-Typ | `OrderDetails/OrderType` = **FUL** | `OrderDetails/AdminOrderType` = **BTU** |
| Order-Parameter | `FULOrderParams` | `BTUOrderParams` (BTF) |
| ES-Schema | S001 (`OrderSignatureData`/`OrderSignature`) | S002 (`OrderSignatureData`) |
| Transaktions-Header | `NumSegments`/`TransactionID`/`SegmentNumber`+`lastSegment` — strukturgleich | dito |

Genau **ein** `OrderData`-Element pro Transfer-Nachricht (Binding) — mehrere Segmente je Nachricht
sind strukturell ausgeschlossen. Die `TransactionID` ist 16 Byte (`hexBinary`); intern wird sie als
Hex-String verschlüsselt (Store-Key).

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Request-XML aus committeten Core-Bindings,
keine proprietären Fixtures):

- `UploadTransactionTests` (`[Theory]` über H003/H004/H005) — **Happy Path 1 Segment** (Init →
  `TransactionID` + `phase=Initialisation`; Transfer → `phase=Transfer`, Order-Data im Store ==
  Original) und **N Segmente** (Reassemblierung über mehrere Nachrichten). Der Transfer-Response
  wird deserialisiert und `TransactionPhase == Transfer` geprüft (schließt den
  `host.md`-Serialisierungs-Vorbehalt). Negativfälle: unbekannte `TransactionID` (`091101`),
  Segmentnummer > `NumSegments` (`091104`), Duplikat (`091103`), `lastSegment` vor Vollzähligkeit
  (`011101`), Teilnehmer nicht `Ready` (`091002`), unentschlüsselbare Order-Data (`090004`).
- `UploadTransactionStoreTests` — `InMemoryUploadTransactionStore` (Create/TryGet/Remove/Count,
  Hex-Keying, Duplikat-Create, null-Guards) und die Segmentpuffer-Logik von `UploadTransaction`
  (Buffered/Ready/Duplicate/Underrun, Reassemblierung in Segmentreihenfolge).
- `EbicsEndpointIntegrationTests` — Upload über den HTTP-Endpoint (`WebApplicationFactory`,
  `POST /ebics`): Init → `TransactionID` aus der Antwort → Transfer → `EBICS_OK`, Order-Data im Store.

## Verwandte Doku

- [Segmentierung, Kompression & Base64-Pipeline](segmentation.md) — die genutzten Byte-Primitiven
- [Verschlüsselung E002](../protocol/encryption-e002.md) — Transaktionsschlüssel- & Order-Data-Entschlüsselung
- [Hostable Server-Grundgerüst](host.md) — Pipeline, Fehlerabbildung, `EbicoServerOptions`
- [Schlüsselwechsel & Sperrung (HCA/HCS/SPR/HSA)](hca-hcs-spr-hsa.md) — Vorbild für den einphasigen, verschlüsselten Upload
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — die ausgelösten Transaktions-/Segment-Codes
- [ADR-0013 (Upload-Transaktions-Engine)](../adr/0013-upload-transaktions-engine.md) — dedizierte Engine statt Resolver, In-Memory-Transaktionsspeicher
