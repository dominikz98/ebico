# Server: Verteilte elektronische Unterschrift (HVU/HVZ/HVD/HVT/HVE/HVS)

> Umsetzung von **Issue #42** (Milestone M5 — Server: Orders & BTF). Diese Seite beschreibt die
> **verteilte elektronische Unterschrift** (EDS / VEU): Aufträge, die nach dem Upload noch weitere
> Unterschriften brauchen, werden serverseitig zwischengelagert; weitere Teilnehmer sehen sie (HVU/HVZ),
> rufen Details ab (HVD/HVT) und zeichnen (HVE) bzw. stornieren (HVS) sie.
>
> Bewusst **enthalten**: die sechs Order-Typen — **HVU** (Übersicht offener Aufträge), **HVZ** (Übersicht
> mit Zusatzdetails), **HVD** (Auftragsstatus/-detail), **HVT** (Transaktionsdetails), **HVE** (Zeichnung),
> **HVS** (Stornierung) über alle drei Versionen; ein neuer, langlebiger, partner-bezogener **VEU-Speicher**
> (`IOpenVeuStore`/`InMemoryOpenVeuStore`); der versionsbewusste Core-Builder (`VeuResponseBuilder`) und die
> DTOs (`VeuOrderView`/`VeuSignerView`); der komplette **Mehr-Unterschriften-Workflow im Server-Zustand**
> (Parken → Zeichnen → Freigabe/Storno) inkl. `pain.002`-Ablage bei Freigabe; neue `EbicsEventType`-Werte
> (`VeuPending`/`VeuSigned`/`VeuReleased`/`VeuCancelled`) und der Returncode `091121`
> `EBICS_INVALID_ORDER_IDENTIFIER`.
> Bewusst **noch nicht**: die **kryptografische Prüfung** der elektronischen Unterschriften (die ES wird
> mitgeführt, aber nicht verifiziert — durchgängiger Spec-Vorbehalt seit #32); die **vollständige
> ISO-20022-Einzeltransaktions-Zerlegung** in HVT (hier auftrags-summarisch); die bank-seitigen
> **Konto-Unterschriftsregeln** (hier über ein Request-Flag + feste Anzahl approximiert); die **X002-Signatur**
> der Antwort (M4).

## Zweck

Bei der verteilten elektronischen Unterschrift (EBICS *EDS — Distributed Electronic Signatures*) reicht ein
Teilnehmer einen Auftrag (typischerweise einen Zahlungsauftrag) ein, ohne dass alle erforderlichen
bank-technischen Unterschriften bereits vorliegen. Der Auftrag wird serverseitig in der
„Unterschriftenmappe" **zwischengelagert**, bis genügend Teilnehmer ihn gezeichnet haben; erst dann wird er
freigegeben. Andere Teilnehmer benötigen dafür vier Download-Orders (**HVU/HVZ** Übersicht, **HVD/HVT**
Detail) und zwei Upload-Orders (**HVE** zeichnen, **HVS** stornieren).

Wie die Status-/Protokoll-Orders ([#41](status-protocol-orders.md)) bleiben alle sechs in H005 klassische
**AdminOrderTypes** (kein BTF-Service, siehe [BTF-Framework](btf-framework.md)). Sie docken an die
vorhandenen Transaktions-Engines an; strukturell neu ist allein der langlebige Speicher für offene Aufträge.

## Der VEU-Speicher (`IOpenVeuStore`)

Anders als die transienten Transaktions-Speicher (Idle-Timeout, [#35](transaction-recovery.md)) lebt ein
offener VEU-Auftrag **partner-bezogen** bis zur Vollzeichnung (Freigabe) oder Stornierung. Der Speicher ist
nach `(HostId, PartnerId, OrderId)` verschlüsselt; die **OrderId** (4 Zeichen, Muster `[A-Z][A-Z0-9]{3}`)
vergibt der Speicher beim Hinzufügen (führendes `V` + laufende Base-36-Nummer). Ein `OpenVeuOrder` hält:
Order-Daten (+ Größe, SHA-256-Digest), Order-Typ, Einreicher (`OriginatorInfo`), Anzahl geforderter/geleisteter
Unterschriften und die Liste der bereits Zeichnenden (`SignerInfo`). Default: `InMemoryOpenVeuStore`,
pluggbar via `TryAddSingleton`.

## Einreichungs-Konventionen & Routing

Die Order-Codes werden **direkt** eingereicht (`AdminOrderType` in H005, `OrderType` in H003/H004);
`BtfOrderTypeCatalog.ResolveUpload/DownloadOrderType` reicht den rohen Code durch. HVU/HVZ/HVD/HVT laufen als
**Downloads**, HVE/HVS als **Uploads**:

| Order | Richtung | Engine-Erkennung | OrderParams |
| --- | --- | --- | --- |
| HVU / HVZ | Download | `DownloadTransactionEngine.IsDownloadOrderType` (`VeuOrderTypes.IsVeuDownloadOrderType`) | bar (keine) |
| HVD / HVT | Download | dito | `HV[DT]OrderParams/OrderID` (Ziel-Auftrag) |
| HVE / HVS | Upload | `UploadTransactionEngine.IsUploadOrderType` (`VeuOrderTypes.IsVeuUploadOrderType`) | `HV[ES]OrderParams/OrderID` (Ziel-Auftrag) |

Die Engines wurden um die OrderID-Extraktion aus den `Hv*OrderParams` erweitert (`DownloadOrderRequest.OrderId`
bzw. `UploadOrderContext.OrderId`). Die Erzeugung/Verarbeitung ist auf pluggbare Processoren verteilt: ein
neuer `VeuOverviewDownloadProcessor` (HVU/HVZ/HVD/HVT, projiziert aus dem Speicher) und ein neuer
`VeuSignatureUploadProcessor` (HVE/HVS). Die Upload-Engine nimmt dafür jetzt — symmetrisch zur Download-Engine
— `IEnumerable<IUploadOrderProcessor>` und wählt den ersten passenden `CanProcess`.

## Ablauf

### 1. Parken (Upload eines Auftrags zur verteilten Zeichnung)

Ein Zahlungsauftrag (CCT/CDD/CDB/CIP) wird für die verteilte Zeichnung eingereicht, wenn das Request-Signal
gesetzt ist (siehe [Trigger](#park-trigger)). Der `SepaPaymentUploadProcessor` validiert die pain-Payload wie
gewohnt und legt den Auftrag dann — statt sofort den `pain.002` abzulegen — im `IOpenVeuStore` ab:

| Schritt | Aktion |
| --- | --- |
| 1. Validieren | `SepaPaymentValidator` (unverändert); ungültig → `090004`, `OrderRejected` |
| 2. Erst-Unterschrift | trägt der Einreicher eine bank-technische Klasse (E/A/B) für den Order-Typ? → erste `SignerInfo` |
| 3. Parken | `OpenVeuOrder` anlegen (`NumSigRequired` = `EbicoServerOptions.VeuRequiredSignatures`, Default 2), Event `VeuPending` |

Erfüllt die Erst-Unterschrift bereits die geforderte Anzahl (z. B. Klasse E bei `VeuRequiredSignatures=1`),
wird der Auftrag **nicht** geparkt, sondern sofort wie ein gewöhnlicher Upload freigegeben.

### 2. Sehen & prüfen (HVU/HVZ/HVD/HVT)

Der `VeuOverviewDownloadProcessor` projiziert die offenen Aufträge des Partners über den `VeuResponseBuilder`
in die versionsspezifischen Bindings:

- **HVU/HVZ** listen **alle** offenen Aufträge des Partners (leere Liste → leeres, gültiges Dokument, kein
  Fehler). HVZ trägt zusätzlich Digest/Größen-Details.
- **HVD/HVT** adressieren **einen** Auftrag über die `OrderID` aus den OrderParams. Fehlt die ID oder
  identifiziert sie keinen offenen Auftrag → der Processor dekliniert (`null`) → Engine meldet `090005`.

### 3. Zeichnen (HVE) & Freigabe

Der `VeuSignatureUploadProcessor` verarbeitet ein HVE gegen die referenzierte `OrderID`:

| Schritt | Aktion |
| --- | --- |
| 1. Auflösen | Auftrag per `(Host, Partner, OrderID)`; unbekannt → `091121` |
| 2. Autorisieren | Zeichner muss `Subscriber.CanAuthorize(zugrundeliegender Order-Typ)` erfüllen (E/A/B) → sonst `090003` |
| 3. Zeichnen | Doppelunterschrift desselben Users → `090004`; sonst Unterschrift eintragen, Event `VeuSigned` |
| 4. Freigabe | erreicht `NumSigDone` die geforderte Zahl → `pain.002` für den Einreicher ablegen, Auftrag entfernen, Events `VeuReleased` + `OrderAccepted` |

### 4. Stornieren (HVS)

Ein HVS entfernt den Auftrag (Event `VeuCancelled`). Erlaubt für den **Einreicher** oder einen zur
Zeichnung des zugrundeliegenden Order-Typs berechtigten Teilnehmer (sonst `090003`); ein unbekannter
Auftrag → `091121`. Ein stornierter Auftrag wird **nie** freigegeben (kein `pain.002`).

<a id="park-trigger"></a>

### Park-Trigger

Ob ein Upload in die verteilte Zeichnung geht, entscheidet ein **explizites, nicht-brechendes Request-Signal**
(Default: sofortige Freigabe wie [#39](payment-orders.md)):

| Version | Signal | Default (nicht verteilt) |
| --- | --- | --- |
| H005 | Präsenz von `BTUOrderParams/SignatureFlag` | kein `SignatureFlag` |
| H003/H004 | `OrderAttribute = OZHNN` | `OrderAttribute = DZHNN` |

Ein klassenbasierter Trigger schied aus, weil die #39-Uploads mit Transport-Klasse (T) seeden — er würde sie
fälschlich parken.

### Beispiel — HVU (H005, gekürzt)

```xml
<HVUResponseOrderData xmlns="urn:org:ebics:H005">
  <OrderDetails>
    <Service><ServiceName>SCT</ServiceName><MsgName>pain.001</MsgName></Service>
    <OrderID>V001</OrderID>
    <OrderDataSize>1234</OrderDataSize>
    <SigningInfo readyToBeSigned="true" NumSigRequired="2" NumSigDone="1" />
    <SignerInfo>
      <PartnerID>PARTNER01</PartnerID><UserID>USER01</UserID><Name>Alice</Name>
      <Timestamp>2026-07-15T10:00:00Z</Timestamp>
      <Permission AuthorisationLevel="A" />
    </SignerInfo>
    <OriginatorInfo>
      <PartnerID>PARTNER01</PartnerID><UserID>USER01</UserID><Name>Alice</Name>
      <Timestamp>2026-07-15T10:00:00Z</Timestamp>
    </OriginatorInfo>
  </OrderDetails>
</HVUResponseOrderData>
```

In H003/H004 trägt `OrderDetails` statt `Service` den klassischen `OrderType` (z. B. `CCT`).

## Returncodes & Fehlerfälle

| Situation | Returncode | Order |
| --- | --- | --- |
| Erfolg (Parken / Zeichnen / Freigabe / Storno / Download) | `000000` EBICS_OK | alle |
| unbekannte OrderID | `091121` EBICS_INVALID_ORDER_IDENTIFIER | HVE/HVS |
| Zeichner nicht zeichnungsberechtigt / HVS unberechtigt | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | HVE/HVS |
| Doppelunterschrift / Auftrag bereits vollständig | `090004` EBICS_INVALID_ORDER_DATA_FORMAT | HVE |
| kein offener Auftrag zur OrderID / leere Detail-Anfrage | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | HVD/HVT |
| Order-Typ nicht berechtigt (Engine-Gate) | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | alle |

Die übrigen Transaktions-/Segment-Codes stammen unverändert aus der
[Upload-](upload-transaction.md)/[Download-Transaktion](download-transaction.md).

### ⚠️ Spec-Vorbehalte

- **ES nicht verifiziert.** Die von HVE getragene elektronische Unterschrift wird nicht kryptografisch
  geprüft; „zeichnen" bedeutet, dass ein für den zugrundeliegenden Order-Typ **autorisierter** Teilnehmer
  (E/A/B-Permission) ein HVE eingereicht hat. Der `DataDigest` ist ein einfacher SHA-256 über die Order-Daten,
  nicht der kanonische EBICS-ES-Hash.
- **Park-Trigger & Unterschriftenzahl.** Ob ein Auftrag verteilt zu zeichnen ist, kommt aus dem
  Request-Signal (`SignatureFlag`/`OrderAttribute`), nicht aus bank-seitigen Konto-Unterschriftsregeln; die
  geforderte Zahl ist eine feste Server-Option (`VeuRequiredSignatures`, Default 2).
- **HVT auftrags-summarisch.** HVT liefert einen einzelnen `OrderInfo` (Nachrichtenname des Auftrags), keine
  vollständige ISO-20022-Zerlegung je Einzeltransaktion.
- **Duplikat-/Vollständig-Code.** Für „schon gezeichnet"/„bereits vollständig" gibt es keinen dedizierten
  EBICS-Code; hier `090004` (best-effort).
- **Unsignierte Antwort.** X002 weiterhin zurückgestellt (M4), wie bei den Transaktionen.

## EBICS-Versionsbezug

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Auftragsidentität (Response) | `OrderType` (+ H004 `FileFormat`) | `Service` (BTF `RestrictedServiceType`) |
| Park-Trigger (Upload) | `OrderAttribute = OZHNN` | `BTUOrderParams/SignatureFlag` |
| HVT `OrderInfo` | `OrderFormat` (String) | `MsgName` (`MessageType`) |
| HVZ `AdditionalOrderInfo` | nicht vorhanden | vorhanden |
| Namespace | `http://www.ebics.org/H003` (H003) · `urn:org:ebics:H004` | `urn:org:ebics:H005` |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; keine proprietären Fixtures):

- `Core/Administrative/VeuOrderTypesTests` — Klassifizierung Download/Upload/Negativ.
- `Core/Administrative/VeuResponseBuilderTests` — HVU/HVZ/HVD/HVT über H003/H004/H005 (Namespace, OrderID,
  Signing-/Signer-Info, `Service` vs. `OrderType`), leere Übersicht.
- `Server/OpenVeuStoreTests` — OrderID-Vergabe/Muster, Listing pro Partner, Sign-State-Machine
  (Signatur/Duplikat/Completion), Remove.
- `Server/VeuOrdersTests` — **end-to-end** durch die Pipeline über alle Versionen: Parken → HVU → HVD → HVT →
  HVE → Freigabe (`pain.002` abgelegt, Übersicht leer); leere HVU; HVS-Storno; Negativ: unbekannte OrderID
  (`091121`), nicht zeichnungsberechtigt (`090003`), Doppelunterschrift (`090004`).

## Verwandte Doku

- [Upload-Transaktion](upload-transaction.md) / [Download-Transaktion](download-transaction.md) — die Engines, an denen #42 andockt
- [Upload-Orders: Zahlungsverkehr](payment-orders.md) — die geparkten Aufträge und die `pain.002`-Ablage bei Freigabe (#39)
- [Status- & Protokoll-Orders](status-protocol-orders.md) — das Schwester-Muster (#41): AdminOrderTypes, pluggbare Download-Processoren
- [Ereignis-/Protokollspeicher (`IEventLog`)](event-log.md) — die VEU-Ereignisse (`VeuPending`/`VeuSigned`/`VeuReleased`/`VeuCancelled`)
- [BTF-Framework (H005)](btf-framework.md) — Admin- vs. BTF-Order-Typen, Berechtigungsprüfung
- [ADR-0020 (Verteilte elektronische Unterschrift)](../adr/0020-veu-orders.md) — VEU-Speicher, Park-Trigger, Zeichnungs-Autorisierung, Freigabe
