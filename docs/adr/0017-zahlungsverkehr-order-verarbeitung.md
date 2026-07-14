# 0017 — Zahlungsverkehr-Order-Verarbeitung (Validierung & Statusreport-Ablage)

- Status: accepted
- Datum: 2026-07-14

## Kontext

Nach dem [BTF-Framework (#38)](0016-btf-framework-und-berechtigung.md) und der
[Upload-Transaktions-Engine (#32)](0013-upload-transaktions-engine.md) endete ein Upload damit, dass
die reassemblierte, entschlüsselte, dekomprimierte Klartext-Order-Data auf der `UploadTransaction`
festgehalten wurde — ohne **fachliche** Verarbeitung. Issue #39 liefert die erste konkrete
Order-Verarbeitung: SEPA-Zahlungsverkehr (CCT/CIP → `pain.001`, CDD/CDB → `pain.008`) soll
**validiert** und ein **Statusreport (pain.002) zur späteren Auslieferung abgelegt** werden. Dabei
waren drei Entscheidungen zu treffen: (a) wie tief die pain-Payload validiert wird, (b) was „Ablage zur
späteren Auslieferung (Statusreports)" konkret erzeugt und (c) welche Einreichungs-Konventionen der
Emulator akzeptiert.

## Entscheidung

1. **Strukturelle/semantische Validierung statt XSD.** `EBICO.Core.Payments.SepaPaymentValidator`
   prüft Wohlgeformtheit, den `Document`-Root in der erwarteten ISO-Namespace-Familie
   (`urn:iso:std:iso:20022:tech:xsd:pain.001`/`pain.008`), den Initiation-Root, die Pflichtfelder
   `GrpHdr/MsgId`/`CreDtTm`/`NbOfTxs`, ≥1 `PmtInf` + Transaktion und die zwei Cross-Checks
   (`NbOfTxs` = Anzahl Transaktionen, `CtrlSum` = Summe der `InstdAmt`). **Keine** ISO-20022-XSDs im
   Repo — konsistent mit dem Umgang mit proprietären/externen Schemas
   ([ADR-0003](0003-umgang-mit-proprietaeren-schemas.md), [ADR-0006](0006-generierte-xsd-bindings-committen.md)).
   Elemente werden über **Local Names** gematcht, sodass jede `pain.00x.001.NN`-Revision akzeptiert
   wird. (Verworfen: volle XSD-Validierung — Schema-Beschaffungs-Infrastruktur + CI-Abhängigkeit ohne
   Mehrwert für den Emulator; die pluggbare Prozessor-Abstraktion lässt echtes XSD später nachrüsten.)

2. **pain.002 generieren und via `IDownloadDataProvider` ablegen.** Bei erfolgreicher Validierung baut
   `PainStatusReportBuilder` einen positiven **pain.002** (Group-Status `ACCP`, Echo von
   `OrgnlMsgId`/`OrgnlMsgNmId`) und der `SepaPaymentUploadProcessor` legt ihn über
   `IDownloadDataProvider.EnqueueAsync` unter `EbicoServerOptions.PaymentStatusReportOrderType`
   (Default `"PSR"`) für den einreichenden Teilnehmer ab. Damit ist die Upload→Statusreport-Schleife
   geschlossen und über Provider/Admin-API beobachtbar. (Verworfen: nur die Roh-Payload ablegen — der
   Statusreport ist der eigentliche fachliche Mehrwert.)

3. **Alle drei Einreichungs-Konventionen akzeptieren.** H005 `BTU` + BTF, H003/H004 klassischer
   Order-Code **direkt** (`OrderType="CCT"`), und H003/H004 generisches `FUL` +
   `FULOrderParams/FileFormat`. Der effektive Order-Code wird zentral über
   `BtfOrderTypeCatalog.ResolveUploadOrderType(orderType, btf, fileFormat)` aufgelöst und **vor** der
   Berechtigungsprüfung verwendet (Fix: FUL wird gegen `CCT`, nicht gegen `FUL` autorisiert).

4. **Pluggbarer `IUploadOrderProcessor` statt Inline-Logik.** Die Engine ruft nach dem Dekodieren einen
   per DI registrierten Prozessor (Default `SepaPaymentUploadProcessor`, `TryAddSingleton`). Order-Typen,
   die der Prozessor nicht kennt, behalten das bisherige Verhalten (nur Klartext festhalten). Der
   aufgelöste Order-Code wird bei der Init auf der `UploadTransaction` mitgespeichert, weil die
   Transfer-Phase keinen Order-Typ mehr trägt.

## Konsequenzen

- Ein ungültiger Payload → `EBICS_INVALID_ORDER_DATA_FORMAT` (`090004`), **keine** Ablage, `OrderRejected`-Event.
  Ein gültiger → `000000`, pain.002 abgelegt, `OrderAccepted`-Event.
- `PaymentStatusReportOrderType` (`"PSR"`) ist ein **Best-Effort-Platzhalter** bis zur offiziellen
  External Code List; das **End-to-End-Herunterladen** des Statusreports (Mapping FDL-`FileFormat`/
  BTD-BTF → PSR-Queue) folgt mit den [Download-Orders (#40)](../server/download-transaction.md), da die
  Download-Engine heute nach dem rohen Order-Typ (FDL/BTD) entnimmt.
- Die ES/`SignatureFlag`-Prüfung bleibt weiterhin offen (konsistent mit #32).
- `CIP` wurde dem `BtfOrderTypeCatalog` ergänzt (SCT/`INST`/pain.001), eindeutig von CCT über die
  Service-Option unterscheidbar.

## Alternativen

- **Volle ISO-20022-XSD-Validierung** — abgelehnt (s. o.), aber durch die pluggbare Validierung nachrüstbar.
- **Nur Roh-Payload ablegen** statt pain.002 zu erzeugen — abgelehnt (s. o.).
- **Order-spezifische einphasige Handler** (wie INI/HIA) statt eines Prozessors auf der Engine —
  abgelehnt: Zahlungsverkehr ist ein mehrphasiger, segmentierter Upload; der Andockpunkt ist der
  Abschluss der Transaktion, nicht der Single-Shot-Resolver.
