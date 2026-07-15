# Server: Upload-Orders — Zahlungsverkehr (CCT/CDD/CDB/CIP)

> Umsetzung von **Issue #39** (Milestone M5 — Server: Orders & BTF). Diese Seite beschreibt die
> erste **fachliche Order-Verarbeitung** auf der [Upload-Transaktion](upload-transaction.md): das
> **Validieren** hochgeladener SEPA-Zahlungsverkehr-Payloads und das **Ablegen eines
> Statusreports (pain.002) zur späteren Auslieferung**.
>
> Bewusst **enthalten**: der pluggbare `IUploadOrderProcessor` (Default `SepaPaymentUploadProcessor`),
> die strukturelle pain-Validierung (`EBICO.Core.Payments.SepaPaymentValidator`), der pain.002-Builder
> (`PainStatusReportBuilder`), die Auflösung des effektiven Order-Codes über alle drei
> Einreichungs-Konventionen (`BtfOrderTypeCatalog.ResolveUploadOrderType`) und das Ablegen des
> Statusreports über den `IDownloadDataProvider`. Order-Typen: **CCT** (SEPA Credit Transfer /
> `pain.001`), **CIP** (SEPA Instant / `pain.001`), **CDD** (SEPA Direct Debit CORE / `pain.008`),
> **CDB** (SEPA Direct Debit B2B / `pain.008`).
> Bewusst **noch nicht**: eine echte **ISO-20022-XSD-Validierung** (strukturell/semantisch statt XSD);
> die **ES/`SignatureFlag`-Prüfung** (weiterhin zurückgestellt, konsistent mit
> [#32](upload-transaction.md)); das **End-to-End-Herunterladen** des Statusreports (Mapping
> FDL-`FileFormat`/BTD-BTF → `PSR`-Queue): die Generate-on-Demand-Download-Maschine kam mit den
> [Download-Orders (#40)](statement-orders.md), das `PSR`-Mapping selbst bleibt aber offen (kein
> BTF/Order-Typ zeigt auf `PSR`).

## Zweck

Ein Zahlungsverkehr-Auftrag ist ein **generischer, segmentierter Upload** (kein eigener Handler): er
läuft über die [Upload-Transaktion](upload-transaction.md) (FUL/BTU) bzw. — bei den klassischen
Auftragsarten — direkt über den Order-Code. Nach dem Reassemblieren/Entschlüsseln/Dekomprimieren lag
die Klartext-Payload bisher nur auf der Transaktion. #39 hängt an dieser Stelle die **fachliche
Verarbeitung** ein: die Payload wird gegen die erwartete pain-Nachricht geprüft und — bei Erfolg — ein
positiver **pain.002 Customer Payment Status Report** erzeugt und zur späteren Auslieferung abgelegt.

## Einreichungs-Konventionen & Routing

Der Emulator akzeptiert alle drei üblichen EBICS-Konventionen; der **effektive** Order-Code wird zentral
über `BtfOrderTypeCatalog.ResolveUploadOrderType(orderType, btf, fileFormat)` aufgelöst (Reihenfolge:
BTF → FileFormat → roher Code):

| Version | Konvention | Beispiel | Auflösung |
| --- | --- | --- | --- |
| H005 | `AdminOrderType=BTU` + `BTUOrderParams/Service` (BTF) | Service `SCT`/`pain.001` | → `CCT` |
| H003/H004 | klassischer `OrderType` **direkt** | `OrderType=CCT` | → `CCT` |
| H003/H004 | generisches `OrderType=FUL` + `FULOrderParams/FileFormat` | `FileFormat=pain.001.001.09` | → `CCT` |

Die Routing-Erkennung (`UploadTransactionEngine.IsUploadOrderType`) kennt neben `FUL`/`BTU` jetzt auch
die direkten Upload-Codes (`BtfOrderTypeCatalog.IsUploadOrderType`). Der aufgelöste Code wird **vor** der
Berechtigungsprüfung ([#38](btf-framework.md)) verwendet und auf der `UploadTransaction`
(`EffectiveOrderType`) mitgespeichert, weil die Transfer-Phase keinen Order-Typ mehr trägt.

> **Hinweis zur FUL-FileFormat-Auflösung:** CDD (CORE) und CDB (B2B) tragen beide `pain.008`; aus dem
> FileFormat allein ist die Service-Option nicht ableitbar — der un-optionierte Default (CDD) gewinnt.
> Für B2B über FUL wäre ein expliziter Marker nötig (Best-Effort, siehe [ADR-0017](../adr/0017-zahlungsverkehr-order-verarbeitung.md)).

## Ablauf

Die Auftragstyp-Erkennung/Autorisierung passiert in der **Initialisation**, die Verarbeitung beim
**letzten Segment** der **Transfer**-Phase (dort liegt die vollständige Payload vor):

| Schritt | Phase | Aktion |
| --- | --- | --- |
| 1. Auflösen | Init | effektiven Order-Code bestimmen (BTF/FileFormat/direkt), gegen Berechtigung prüfen (sonst `090003`), auf der Transaktion ablegen |
| 2. Dekodieren | Transfer (last) | Reassemble → E002-Decrypt → Dekompress (Fehler → `090004`) |
| 3. Verarbeiten | Transfer (last) | ist der Order-Code ein Zahlungsverkehr-Typ, ruft die Engine den `IUploadOrderProcessor` |
| 4a. Validieren | — | `SepaPaymentValidator.Validate(orderType, payload)` — ungültig → `090004`, **keine** Ablage, `OrderRejected`-Event |
| 4b. Ablegen | — | pain.002 bauen (`OrgnlMsgId`/`OrgnlMsgNmId`, `GrpSts=ACCP`) und via `IDownloadDataProvider.EnqueueAsync(subscriber, "PSR", …)` ablegen, `OrderAccepted`-Event |
| 5. Antwort | Transfer | `ebicsResponse`, `phase=Transfer`, `EBICS_OK` (bzw. `090004` bei Reject) |

### Validierung (strukturell/semantisch)

`SepaPaymentValidator` (in `EBICO.Core.Payments`) prüft — **ohne** XSD, Elemente über Local Names:

- Wohlgeformtes XML; `Document`-Root in der erwarteten ISO-Namespace-Familie
  (`urn:iso:std:iso:20022:tech:xsd:pain.001` bzw. `…pain.008`);
- Initiation-Root (`CstmrCdtTrfInitn` / `CstmrDrctDbtInitn`);
- `GrpHdr/MsgId` und `GrpHdr/CreDtTm` (nicht leer), `GrpHdr/NbOfTxs`;
- ≥1 `PmtInf` und ≥1 Transaktion (`CdtTrfTxInf` / `DrctDbtTxInf`);
- **Cross-Check 1:** `NbOfTxs` == Anzahl Transaktionen;
- **Cross-Check 2:** falls `CtrlSum` vorhanden: == Summe der `InstdAmt`.

```xml
<!-- pain.001 (CCT), gekürzt -->
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.001.001.09">
  <CstmrCdtTrfInitn>
    <GrpHdr><MsgId>MSG-CCT-1</MsgId><CreDtTm>2026-07-14T10:00:00</CreDtTm>
      <NbOfTxs>2</NbOfTxs><CtrlSum>150.00</CtrlSum></GrpHdr>
    <PmtInf> … <CdtTrfTxInf><Amt><InstdAmt Ccy="EUR">100.00</InstdAmt></Amt> … </CdtTrfTxInf>
                <CdtTrfTxInf><Amt><InstdAmt Ccy="EUR">50.00</InstdAmt></Amt> … </CdtTrfTxInf> </PmtInf>
  </CstmrCdtTrfInitn>
</Document>
```

### Statusreport (pain.002)

`PainStatusReportBuilder` erzeugt einen minimalen, gruppen-bezogenen **pain.002** (Default
`pain.002.001.03`):

```xml
<Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.002.001.03">
  <CstmrPmtStsRpt>
    <GrpHdr><MsgId>PSR-…</MsgId><CreDtTm>2026-07-14T10:00:00Z</CreDtTm></GrpHdr>
    <OrgnlGrpInfAndSts><OrgnlMsgId>MSG-CCT-1</OrgnlMsgId>
      <OrgnlMsgNmId>pain.001.001.09</OrgnlMsgNmId><GrpSts>ACCP</GrpSts></OrgnlGrpInfAndSts>
  </CstmrPmtStsRpt>
</Document>
```

Er wird unter `EbicoServerOptions.PaymentStatusReportOrderType` (Default `"PSR"`) für den einreichenden
Teilnehmer abgelegt und ist über die [Admin-API](master-data.md) (`GET
…/subscribers/{userId}/downloads/PSR`) bzw. den `IDownloadDataProvider` beobachtbar.

## Returncodes & Fehlerfälle

| Situation | Returncode | Ablage |
| --- | --- | --- |
| Erfolg (validiert, Statusreport abgelegt) | `000000` EBICS_OK | Header + Body |
| pain-Payload ungültig (Struktur/Cross-Check) | `090004` EBICS_INVALID_ORDER_DATA_FORMAT | Body |
| keine Berechtigung für den (aufgelösten) Order-Typ | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | Body |

Die übrigen Transaktions-/Segment-Codes (`091101`/`091104`/…) stammen unverändert aus der
[Upload-Transaktion](upload-transaction.md).

### ⚠️ Spec-Vorbehalte

- **Keine echte XSD-Validierung.** Struktur/Semantik statt ISO-20022-XSD (ADR-0017); pluggbar für
  echtes XSD über einen ersetzten `IUploadOrderProcessor`/Validator.
- **ES/`SignatureFlag` weiterhin ungeprüft.** Die Payload ist entschlüsselt, aber nicht authentifiziert
  (konsistent mit [#32](upload-transaction.md)).
- **Statusreport-Download offen.** `PaymentStatusReportOrderType` (`"PSR"`) ist ein Best-Effort-
  Platzhalter. Die [Download-Orders (#40)](statement-orders.md) haben die Download-Engine so umgestellt,
  dass sie nach dem **aufgelösten** Order-Typ entnimmt (statt nur nach rohem FDL/BTD); es gibt aber weiterhin
  **kein** BTF/Order-Typ, der auf die `PSR`-Queue zeigt, sodass der Statusreport nur über die
  [Admin-API](master-data.md) beobachtbar ist. Das `PSR`-Mapping bleibt ein Folgeschritt.
- **FUL-B2B-Ambiguität.** CDD/CDB teilen `pain.008`; über FUL/FileFormat gewinnt der CORE-Default (CDD).
- **pain.002-Version.** Fest `pain.002.001.03` statt strikt an die Upload-Version gekoppelt.

## EBICS-Versionsbezug

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Auftragsidentität | `OrderType` direkt **oder** `FUL` + `FULOrderParams/FileFormat` | `AdminOrderType=BTU` + `BTUOrderParams/Service` (BTF) |
| Auflösung → Code | direkt / FileFormat-Familie → CCT/CDD/CDB/CIP | BTF → klassischer Code (Katalog) |
| pain-Payload | identisch (`pain.001`/`pain.008`, versionsagnostisch geprüft) | dito |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; pain-XML aus dem committeten
`Infrastructure/PainSamples`-Builder, keine proprietären Fixtures):

- `Core/Payments/SepaPaymentValidatorTests` — gültige `pain.001`/`pain.008`; Negativfälle: falsche
  Nachrichtenfamilie, fehlende `MsgId`, `NbOfTxs`-Mismatch, `CtrlSum`-Mismatch, kein `PmtInf`,
  malformed XML, unbekannter Order-Typ.
- `Core/Payments/PainStatusReportBuilderTests` — pain.002 echot `OrgnlMsgId`/`OrgnlMsgNmId`, `GrpSts=ACCP`.
- `Core/Btf/BtfOrderTypeCatalogTests` — CIP-Seed, `IsUploadOrderType`, `TryGetOrderTypeByFileFormat`,
  `ResolveUploadOrderType` (alle drei Konventionen).
- `Server/PaymentUploadTests` (`[Theory]` über H003/H004/H005) — CCT/CDD/CDB **end-to-end** durch die
  Pipeline (H005 BTU+BTF, H003/H004 direkt **und** FUL+FileFormat): `000000`, Statusreport im Provider
  abgelegt, dequeuter pain.002 mit passender `OrgnlMsgId`; ungültige Payload → `090004`, nichts abgelegt,
  Transaktion nicht abgeschlossen.

## Verwandte Doku

- [Upload-Transaktion (Initialisation + Transfer)](upload-transaction.md) — die Empfangsmaschine, an der #39 andockt
- [BTF-Framework (H005)](btf-framework.md) — BTF↔OrderType-Katalog, Berechtigungsprüfung
- [Download-Transaktion](download-transaction.md) — der Ablage-/Auslieferungskanal (`IDownloadDataProvider`)
- [Download-Orders: Kontoauszüge & Reports](statement-orders.md) — die Download-Gegenseite (#40); stellte die Engine auf Entnahme nach aufgelöstem Order-Typ um (`PSR`-Mapping weiter offen)
- [Ereignis-/Protokollspeicher (IEventLog)](event-log.md) — `OrderAccepted`/`OrderRejected`-Ereignisse
- [Stammdatenverwaltung](master-data.md) — Berechtigungen, Admin-API (Download-Queue)
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — `090004`/`090003`
- [ADR-0017 (Zahlungsverkehr-Order-Verarbeitung)](../adr/0017-zahlungsverkehr-order-verarbeitung.md) — Validierungstiefe, Statusreport-Ablage, Routing
