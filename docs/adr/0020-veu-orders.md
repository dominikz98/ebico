# 0020 — Verteilte elektronische Unterschrift (VEU-Speicher, Park-/Zeichnungs-Workflow)

- Status: accepted
- Datum: 2026-07-16

## Kontext

Issue #42 (Milestone M5) fordert die **verteilte elektronische Unterschrift** (EBICS *EDS*): HVU/HVZ
(Übersicht offener Aufträge), HVD/HVT (Detail) sowie HVE (zeichnen) und HVS (stornieren). Kern ist ein
**Mehr-Unterschriften-Workflow im Server-Zustand** — ein hochgeladener Auftrag, der noch weitere
Unterschriften braucht, muss zwischengelagert und von weiteren Teilnehmern gezeichnet werden.

Vorhanden waren: die [Upload-](../server/upload-transaction.md) und
[Download-Transaktion](../server/download-transaction.md) (#32/#33), das Processor-Muster
(`IUploadOrderProcessor`/`IDownloadOrderProcessor`), die Zahlungs-Verarbeitung mit `pain.002`-Ablage (#39,
[ADR-0017](0017-zahlungsverkehr-order-verarbeitung.md)), der [`IEventLog`](../server/event-log.md) (#69) und
— committet — die generierten VEU-Bindings (`HVU/HVZ/HVD/HVT/HVE/HVS…`) für alle drei Versionen. **Nicht**
vorhanden: ein serverseitiger Zustand für „hochgeladen, aber noch nicht (fertig) gezeichnet" — die bisherige
Verarbeitung schließt einen Upload sofort ab.

Zu entscheiden war: (1) wo offene Aufträge gehalten werden, (2) woran der Server erkennt, dass ein Upload
verteilt zu zeichnen ist (ohne die #39-Uploads zu brechen), (3) wer zeichnen darf, (4) was bei Vollzeichnung
passiert, (5) wie die sechs Orders angebunden werden.

## Entscheidung

1. **Neuer, langlebiger VEU-Speicher** `IOpenVeuStore` (Default `InMemoryOpenVeuStore`), **partner-bezogen**
   nach `(HostId, PartnerId, OrderId)` — bewusst getrennt von den transienten Transaktions-Speichern (kein
   Idle-Timeout): ein offener Auftrag lebt bis Freigabe oder Storno. Die **OrderId** (4 Zeichen,
   `[A-Z][A-Z0-9]{3}`) vergibt der Speicher (führendes `V` + Base-36-Zähler). Ein `OpenVeuOrder` hält
   Order-Daten (+ SHA-256-Digest), Order-Typ, Einreicher, geforderte/geleistete Unterschriften und die
   Zeichner-Liste; die Sign-/Storno-Übergänge sind im Speicher gekapselt (`TrySignAsync`/`TryCancelAsync`).

2. **Park-Trigger als explizites Request-Signal** (Default: sofortige Freigabe wie #39): H005 die Präsenz von
   `BTUOrderParams/SignatureFlag`, H003/H004 `OrderAttribute=OZHNN`. Ein klassenbasierter Trigger schied aus,
   weil die #39-Uploads mit Transport-Klasse (T) seeden — er würde sie fälschlich parken. Der
   `SepaPaymentUploadProcessor` validiert die pain-Payload unverändert und parkt danach (statt `pain.002`
   abzulegen), sofern das Signal gesetzt ist; die Anzahl geforderter Unterschriften ist die feste Option
   `EbicoServerOptions.VeuRequiredSignatures` (Default 2), die Erst-Unterschrift ist die bank-technische
   Klasse (E/A/B) des Einreichers für den Order-Typ.

3. **Zeichnungs-Autorisierung über das vorhandene Signaturklassen-Modell**: ein HVE wird nur akzeptiert, wenn
   der Zeichner `Subscriber.CanAuthorize(zugrundeliegender Order-Typ)` erfüllt (hält E/A/B), sonst `090003`.
   Doppelunterschrift desselben Users → `090004`, unbekannte OrderId → neuer Returncode `091121`
   `EBICS_INVALID_ORDER_IDENTIFIER`. HVS darf der Einreicher oder ein zeichnungsberechtigter Teilnehmer.

4. **Freigabe = `pain.002`-Ablage für den Einreicher** (symmetrisch zum Sofort-Accept aus #39): erreicht die
   Unterschriftenzahl `VeuRequiredSignatures`, wird der Auftrag aus dem Speicher entfernt und der positive
   `pain.002` via `IDownloadDataProvider` abgelegt (gemeinsamer Helfer `PaymentStatusReportFiling`, den auch
   der Sofort-Accept-Pfad nutzt); Ereignisse `VeuReleased` + `OrderAccepted`. Parken/Zeichnen/Storno schreiben
   `VeuPending`/`VeuSigned`/`VeuCancelled` (neue `EbicsEventType`-Werte).

5. **Anbindung an die bestehenden Engines**: HVU/HVZ/HVD/HVT als Download über einen neuen
   `VeuOverviewDownloadProcessor` (projiziert den Speicher via versionsbewusstem `VeuResponseBuilder`),
   HVE/HVS als Upload über einen neuen `VeuSignatureUploadProcessor`.
   `IsUpload/IsDownloadOrderType` erkennen die Codes zusätzlich (`VeuOrderTypes`). Die OrderID der
   Detail-/Zeichnungs-Orders wird aus den `Hv*OrderParams` extrahiert (neue Felder
   `DownloadOrderRequest.OrderId`/`UploadOrderContext.OrderId`). Die **Upload-Engine** nimmt dafür jetzt —
   symmetrisch zur Download-Engine — `IEnumerable<IUploadOrderProcessor>` und wählt den ersten passenden
   `CanProcess` (bisher genau einer).

## Konsequenzen

- Der Emulator bildet den vollen EDS-Zyklus (Parken → Übersicht → Detail → Zeichnen → Freigabe/Storno) über
  alle drei Versionen ab, end-to-end durch die Pipeline testbar — ohne proprietäre Fixtures.
- Die Umstellung der Upload-Engine auf `IEnumerable<IUploadOrderProcessor>` ist additiv; der SEPA-Processor
  (#39) bleibt registriert, Fremd-Processoren lassen sich via `AddSingleton` ergänzen. Der #39-Upload-Builder
  setzt nun explizit `OrderAttribute=DZHNN` (verhaltensneutral, verhindert Fehl-Parken durch den Enum-Default).
- **Spec-Vorbehalte:** die ES wird nicht verifiziert (Digest = einfacher SHA-256); Park-Trigger und
  Unterschriftenzahl approximieren die bank-seitigen Konto-Unterschriftsregeln; HVT ist auftrags-summarisch
  (keine ISO-20022-Einzeltransaktions-Zerlegung); für „schon gezeichnet"/„bereits vollständig" fehlt ein
  dedizierter EBICS-Code (best-effort `090004`); die Antwort bleibt unsigniert (X002 = M4).

## Alternativen

- **Aufträge im Transaktions-Speicher halten** — falsch, weil dieser idle-timeout-flüchtig und
  transaktions-scoped ist; eine offene VEU muss über Tage partner-weit leben. Verworfen zugunsten eines
  eigenen Speichers (Muster wie `IDownloadDataProvider`).
- **Klassenbasierter Park-Trigger** (T/A/B parken, E sofort) — bricht die #39-Tests (Transport-Klasse) und
  vermischt Berechtigung mit Einreichungsabsicht; verworfen zugunsten des expliziten Request-Signals.
- **HVE/HVS als eigene Order-Handler** (statt Upload-Processor) — hätte die Segment-/Entschlüsselungs-Pipeline
  dupliziert; verworfen zugunsten der bestehenden Upload-Transaktion.
- **Kein `pain.002` bei Freigabe** (nur Entfernen + Event) — einfacher, aber asymmetrisch zum Sofort-Accept
  und ohne Rückmeldung an den Einreicher; verworfen zugunsten der Symmetrie mit #39.
- **VEU nur für H005** — weniger Aufwand, aber die Bindings liegen für alle drei Versionen vor und die übrigen
  M5-Orders sind versionsvollständig; verworfen zugunsten der Parität.
