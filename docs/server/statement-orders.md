# Server: Download-Orders — Kontoauszüge & Reports (STA/VMK/C53/C52/C54)

> Umsetzung von **Issue #40** (Milestone M5 — Server: Orders & BTF). Diese Seite beschreibt die
> **auftragstypspezifische Inhaltsgenerierung** für Kontoauszüge/Reports auf der
> [Download-Transaktion](download-transaction.md): der Emulator erzeugt bei einem Download **on demand**
> synthetische Auszüge in fünf Formaten und liefert sie über die vorhandene Download-Maschine aus.
>
> Bewusst **enthalten**: die fünf Order-Typen **STA** (SWIFT MT940), **VMK** (SWIFT MT942), **C53**
> (camt.053), **C52** (camt.052), **C54** (camt.054); ein **deterministischer synthetischer Generator**
> (`SyntheticStatementGenerator`, „generierbare Testdaten serverseitig") mit gültiger DE-IBAN, laufendem
> Saldo und **Zeitraum-Filter**; die fünf Format-Builder (`Mt940Builder`/`Mt942Builder`/`Camt05xBuilder`);
> das **ZIP-Verpacken** (`StatementZipContainer`, BTF `Container=Zip`); der pluggbare
> `IDownloadOrderProcessor` (Default `StatementDownloadProcessor`); die Auflösung des effektiven
> Order-Codes über alle drei Konventionen (`BtfOrderTypeCatalog.ResolveDownloadOrderType`) inkl. des neuen
> **VMK/mt942**-Katalogeintrags; die **Präzedenz** vorab eingestellter Daten vor Generierung.
> Bewusst **noch nicht**: ein echtes **Konten-/Buchungs-Stammdatenmodell** (die Daten sind synthetisch);
> die **camt-Versionswahl** ist fest `camt.05x.001.08`; das **PSR/pain.002-Download-Mapping** aus
> [#39](payment-orders.md) (kein BTF/Order-Typ zeigt auf die `PSR`-Queue); der Swiss/ISO-CH-Auszug **Z53**;
> die **X002-Signatur** der Antwort (weiterhin M4).

## Zweck

Ein Kontoauszug-/Report-Download ist ein **generischer, segmentierter Download** (kein eigener Handler):
er läuft über die [Download-Transaktion](download-transaction.md) (FDL/BTD) bzw. — bei den klassischen
Auftragsarten — direkt über den Order-Code. Bisher lieferte die Engine nur vorab per Admin-API
eingestellte Roh-Payloads aus und unterschied beim Entnehmen **nicht** zwischen den fachlichen
Order-Typen. #40 hängt an dieser Stelle die **Inhaltsgenerierung** ein: fehlt für den aufgelösten
Order-Typ eine eingestellte Payload, erzeugt der Server einen synthetischen Auszug im passenden Format,
gefiltert auf den angefragten Zeitraum, verpackt ihn in ein ZIP und übergibt ihn der Sendemaschine.

## Einreichungs-Konventionen & Routing

Der Emulator akzeptiert alle drei üblichen EBICS-Konventionen; der **effektive** Order-Code wird zentral
über `BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, btf, fileFormat)` aufgelöst (Reihenfolge:
BTF → FileFormat → roher Code):

| Version | Konvention | Beispiel | Auflösung |
| --- | --- | --- | --- |
| H005 | `AdminOrderType=BTD` + `BTDOrderParams/Service` (BTF) | Service `EOP`/`camt.053` | → `C53` |
| H003/H004 | generisches `OrderType=FDL` + `FDLOrderParams/FileFormat` | `FileFormat=camt.053` | → `C53` |
| H003/H004 | klassischer `OrderType` **direkt** | `OrderType=STA` | → `STA` |

Die Routing-Erkennung (`DownloadTransactionEngine.IsDownloadOrderType`) kennt neben `FDL`/`BTD` jetzt auch
die direkten Download-Codes (`BtfOrderTypeCatalog.IsDownloadOrderType`: STA/VMK/C53/C52/C54). Der
aufgelöste Code wird **vor** der Berechtigungsprüfung ([#38](btf-framework.md)) verwendet und als
Queue-/Generierungs-Schlüssel weitergereicht.

Der **BTF-Katalog** wurde um **VMK** ergänzt (`STM`/`mt942`); STA/C53/C52/C54 waren bereits mit #38
geseedet. Da `TryGetOrderType` auf Service **und** MsgName-Familie matcht, kollidieren `mt942`↔VMK und
`camt.052`↔C52 trotz gemeinsamen Service `STM` nicht.

## Ablauf

Auflösung, Autorisierung und Bereitstellung passieren in der **Initialisation**; Transfer/Receipt arbeiten
unverändert auf der erzeugten Payload (siehe [Download-Transaktion](download-transaction.md)):

| Schritt | Aktion |
| --- | --- |
| 1. Auflösen | effektiven Order-Code bestimmen (BTF/FileFormat/direkt); `DateRange` aus `FDLOrderParams`/`StandardOrderParams` (H003/H004) bzw. `BTDOrderParams` (H005) extrahieren |
| 2. Autorisieren | `Subscriber.HasPermissionFor(effektiverCode)` — sonst `090003` |
| 3a. Entnehmen | Queue nach dem **aufgelösten** Code (z. B. `C53`) probieren |
| 3b. Kompat-Probe | falls leer und Code ≠ roher OrderType: Queue nach `FDL`/`BTD` probieren (Rückwärtskompatibilität) |
| 3c. Generieren | falls weiter leer und `StatementDownloadProcessor.CanProcess`: synthetischen Auszug erzeugen (Zeitraum-gefiltert), ZIP-verpacken |
| 4. Senden | Komprimieren (`EbicsCompression`) → E002-Verschlüsseln → Segmentieren → Segment 1 + `NumSegments` |

Die **Präzedenz** ist damit: eingestellte Daten (Admin-API/Re-Enqueue) schlagen die Generierung. Der
tatsächlich getroffene Schlüssel wird auf der Transaktion gemerkt, sodass ein negativer Receipt bzw. eine
Eviction die Daten wieder unter demselben Schlüssel einstellen.

Die Schichtung entspricht der realen EBICS-Praxis: der ausgelieferte Chiffretext ist
`base64(E002(zlib(zip(dokument))))` — das Business-Dokument liegt in einem ZIP (BTF `Container=Zip`), das
die Transportkompression der Engine zusätzlich zlib-komprimiert.

### Beispiel — MT940 (STA)

```text
:20:EBICO260731
:25:DE02120300000000202051
:28C:195/1
:60F:C260701EUR1000,00
:61:2607020702C200,00NTRFREF000001
:86:Rechnung 1 Kunde AG
:62F:C260731EUR1150,50
```

MT942 (VMK) trägt statt `:60F:`/`:62F:` einen Floor-Limit (`:34F:`), eine Zeitangabe (`:13D:`) und die
Summen `:90D:`/`:90C:`.

### Beispiel — camt.053 (C53), gekürzt

```xml
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
  <BkToCstmrStmt>
    <GrpHdr><MsgId>EBICO260731</MsgId><CreDtTm>2026-07-31T10:00:00Z</CreDtTm></GrpHdr>
    <Stmt>
      <Id>EBICO260731</Id><ElctrncSeqNb>195</ElctrncSeqNb>
      <FrToDt><FrDtTm>2026-07-01T00:00:00Z</FrDtTm><ToDtTm>2026-07-31T23:59:59Z</ToDtTm></FrToDt>
      <Acct><Id><IBAN>DE02120300000000202051</IBAN></Id><Ccy>EUR</Ccy> … </Acct>
      <Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">1000.00</Amt>
           <CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-07-01</Dt></Dt></Bal>
      <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">1150.50</Amt>
           <CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-07-31</Dt></Dt></Bal>
      <Ntry><Amt Ccy="EUR">200.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts><Cd>BOOK</Cd></Sts> … </Ntry>
    </Stmt>
  </BkToCstmrStmt>
</Document>
```

camt.052 (C52) nutzt die Wurzel `BkToCstmrAcctRpt`/`Rpt` mit einer Interim-Bilanz (`ITBD`); camt.054
(C54) die Wurzel `BkToCstmrDbtCdtNtfctn`/`Ntfctn` **ohne** Bilanzen.

## Returncodes & Fehlerfälle

| Situation | Returncode | Ablage |
| --- | --- | --- |
| Erfolg (generiert oder eingestellt, Segment 1 geliefert) | `000000` EBICS_OK | Header + Body |
| keine Berechtigung für den (aufgelösten) Order-Typ | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | Body |
| nichts verfügbar **und** nicht generierbar | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | Body |
| Payload überschreitet die Segmentgrenze | `091114` EBICS_MAX_SEGMENTS_EXCEEDED | Body |

Die übrigen Transaktions-/Segment-Codes stammen unverändert aus der
[Download-Transaktion](download-transaction.md).

### ⚠️ Spec-Vorbehalte

- **Synthetische Daten.** Konto, Salden und Buchungen sind deterministisch aus dem Teilnehmer-Tripel +
  Zeitraum erzeugt (kein echtes Konten-/Buchungs-Stammdatenmodell) — Testdaten, kein Fachsystem.
- **MT940/MT942-Tag-Syntax.** Minimale, plausible Rendering; **kein** XSD für MT. `:61:`-Grammatik,
  Komma-Dezimal und `:60F:`/`:62F:`-Rahmung sind gegen die offiziellen SWIFT-Feldspezifikationen ungeprüft
  (durch Tests auf exakte Strings fixiert).
- **camt-Version fest `camt.05x.001.08`.** Moderne ISO/CGI-MP-Variante (strukturiertes
  `<Sts><Cd>BOOK</Cd></Sts>`); das klassische DK-Profil `.02` (`<Sts>BOOK</Sts>`) ist nicht implementiert.
- **ZIP-Container vs. Transportkompression.** Das Dokument wird in ein ZIP verpackt und **zusätzlich** von
  der Engine zlib-komprimiert; das genaue Container-Framing (mehrere Tagesdateien, base64-Schachtelung) ist
  gegen den proprietären Annex ungeprüft.
- **PSR/pain.002-Download offen.** Der [#39](payment-orders.md)-Statusreport unter `PSR` bleibt
  download-technisch unerreichbar (kein BTF/Order-Typ mappt auf `PSR`) — eigener Folgeschritt.
- **Unsignierte Antwort.** X002 weiterhin zurückgestellt (M4), wie bei der Download-Transaktion.

## EBICS-Versionsbezug

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Auftragsidentität | `OrderType` direkt (STA/VMK/C5x) **oder** `FDL` + `FDLOrderParams/FileFormat` | `AdminOrderType=BTD` + `BTDOrderParams/Service` (BTF) |
| Auflösung → Code | direkt / FileFormat-Familie → STA/VMK/C53/C52/C54 | BTF → klassischer Code (Katalog) |
| Zeitraum | `FDLOrderParams/DateRange` bzw. `StandardOrderParams/DateRange` | `BTDOrderParams/DateRange` |
| Erzeugte Formate | identisch (MT940/MT942/camt.05x, versionsagnostisch) | dito |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; MT/camt in-code erzeugt, keine proprietären Fixtures):

- `Core/Statements/SyntheticStatementGeneratorTests` — Determinismus, Buchungsdaten im Zeitraum,
  Saldo-Invariante (`Closing = Opening ± Σ`), gültige DE-IBAN (mod 97), unterschiedliche Teilnehmer →
  unterschiedliche Konten, `rangeEnd < rangeStart` → `ArgumentException`.
- `Core/Statements/Mt940BuilderTests` / `Mt942BuilderTests` — Tag-Präsenz, Komma-Dezimal, Leerzeitraum
  (`:60F:`==`:62F:`, keine `:61:`), MT942-Summen und Fehlen der gebuchten Salden.
- `Core/Statements/Camt053/052/054BuilderTests` — Namespace + Wurzel, Bilanzcodes (`OPBD`/`CLBD`; `ITBD`;
  keine bei C54), `Ntry`-Anzahl, `CdtDbtInd`, `Amt/@Ccy`.
- `Core/Statements/StatementZipContainerTests` / `StatementContentFactoryTests` — lesbares/deterministisches
  ZIP, je Order-Typ das erwartete Format.
- `Core/Btf/BtfDownloadOrderTypeTests` — VMK-Mapping, `IsDownloadOrderType`, `ResolveDownloadOrderType`
  (BTF/FileFormat/direkt), `TryGetOrderTypeByFileFormat(Download)`.
- `Server/StatementDownloadTests` — **end-to-end** durch die Pipeline: H005 BTD+camt.053 (Init→Receipt),
  H004 FDL+FileFormat, H004 direkter STA-Code; Zeitraum-Filter, eingestellte Daten schlagen Generierung,
  fehlende Berechtigung → `090003`.

## Verwandte Doku

- [Download-Transaktion (Initialisation + Transfer + Receipt)](download-transaction.md) — die Sendemaschine, an der #40 andockt
- [BTF-Framework (H005)](btf-framework.md) — BTF↔OrderType-Katalog (VMK ergänzt), Berechtigungsprüfung
- [Upload-Orders: Zahlungsverkehr](payment-orders.md) — das Spiegelbild auf der Upload-Seite (`IUploadOrderProcessor`)
- [Stammdatenverwaltung](master-data.md) — Berechtigungen, Admin-API (Download-Queue seeden)
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — `090003`/`090005`/`091114`
- [ADR-0018 (Kontoauszug-/Report-Download-Orders)](../adr/0018-kontoauszug-download-orders.md) — synthetische Generierung, camt `.08`, ZIP-Container
