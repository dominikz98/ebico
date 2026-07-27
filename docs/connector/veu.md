# Connector: Verteilte elektronische Unterschrift (HVU/HVZ/HVD/HVT/HVE/HVS)

> Umsetzung von **Issue #124** ([ADR-0030](../adr/0030-defaults-und-clientseitige-veu-anbindung.md)).
> Serverseitig existiert die VEU seit **#42** ([VEU-Orders](../server/veu-orders.md), ADR-0020) — diese
> Seite beschreibt die **Client**-Seite, die bis #124 fehlte.

Die *verteilte elektronische Unterschrift* (VEU/EDS) ist der Mehr-Augen-Workflow von EBICS: ein Auftrag
wird eingereicht, von der Bank **geparkt** statt ausgeführt, und erst freigegeben, wenn genügend
Berechtigte ihn gezeichnet haben.

## Warum es diese Seite gibt

Die [Order-/BTF-Abdeckungsmatrix](../server/order-coverage-matrix.md) führte HVU–HVS für alle drei
Versionen als ✅ — das galt für den **Server**. Mit dem mitgelieferten Connector war der Workflow in
keiner Version fahrbar; drei Lücken griffen ineinander:

1. **H005-Uploads verlangten einen BTF.** HVE/HVS sind administrative Order-Typen *ohne* BTF, wurden also
   clientseitig mit `EbicsConfigurationException` abgelehnt und erreichten den Draht nie. Der
   Download-Pfad kannte diesen Fall längst — HVU/HVZ funktionierten deshalb.
2. **Es gab kein Feld für die `OrderID`.** HVE/HVS/HVD/HVT beziehen sich auf *einen* geparkten Auftrag.
   Auf H003/H004 gingen sie zwar raus, quittierten aber folgerichtig mit `091121`.
3. **Es ließ sich gar kein Auftrag parken.** Das `OrderAttribute` war in allen Upload-Envelopes hart auf
   `DZHNN` verdrahtet, ein `SignatureFlag` kannte der Connector nicht.

Merksatz: Eine Auftragsart ist aus Anwendersicht erst verfügbar, wenn der mitgelieferte Client sie senden
kann. Die Coverage-Matrix trennt deshalb seit #124 **Server**- und **Client**-Verfügbarkeit.

## Der Ablauf

```csharp
// 1) Auftrag einreichen und zum Parken markieren.
var submitted = await client.Send(new CctUploadRequest
{
    Pain001 = painBytes,
    DistributedSignature = true,   // H005: BTUOrderParams/SignatureFlag · H003/H004: OrderAttribute=OZHNN
});

// 2) Offene Aufträge abholen — hier steht die vom Server vergebene OrderID.
var overview = await client.Send(new HvuDownloadRequest());

// 3) Status eines einzelnen Auftrags (optional).
var detail = await client.Send(new HvdDownloadRequest
{
    Order = new VeuOrderReference { OrderId = "V001", OrderType = "CCT" },
});

// 4) Zeichnen — durch einen ANDEREN Teilnehmer (siehe unten).
var signed = await client.Send(new HveUploadRequest
{
    Order = new VeuOrderReference { OrderId = "V001", OrderType = "CCT" },
});

// ... oder stornieren.
var cancelled = await client.Send(new HvsUploadRequest
{
    Order = new VeuOrderReference { OrderId = "V001", OrderType = "CCT" },
});
```

Erreicht die Zahl der Unterschriften `EbicoServerOptions.VeuRequiredSignatures` (Default **2**), gibt der
Server den Auftrag frei, legt den `pain.002`-Statusreport für den Einreicher ab und entfernt ihn aus dem
VEU-Speicher.

> **Der Einreicher zählt mit.** Hält der einreichende Teilnehmer eine bank-technische Berechtigung
> (E/A/B) für den Auftragstyp, wertet der Emulator seine Einreichung bereits als **erste** Unterschrift
> (`SepaPaymentUploadProcessor`). Ein zweites HVE desselben Teilnehmers wird als Doppelunterschrift
> abgelehnt — der freigebende HVE muss von einem **anderen** Teilnehmer kommen. Genau das ist der Zweck
> der VEU.

## API

| Typ | Auftragsart | Zweck |
| --- | --- | --- |
| `UploadRequest.DistributedSignature` / `CctUploadRequest…` | — | Park-Trigger auf dem einreichenden Upload |
| `HvuDownloadRequest` | `HVU` | Übersicht der offenen Aufträge |
| `HvzDownloadRequest` | `HVZ` | Übersicht mit Zahlungsdetails |
| `HvdDownloadRequest` | `HVD` | Status/Detail eines Auftrags |
| `HvtDownloadRequest` | `HVT` | Transaktionsdetails eines Auftrags 🟡 |
| `HveUploadRequest` | `HVE` | Unterschrift hinzufügen |
| `HvsUploadRequest` | `HVS` | Auftrag stornieren/ablehnen |

Alle sechs sind über `AddEbicoUpload()` / `AddEbicoDownload()` registriert — kein eigenes
`AddEbicoVeu()`, weil sie sich Executor und Envelope-Builder mit den übrigen Orders teilen.

### `VeuOrderReference`

Benennt den geparkten Auftrag. Nur `OrderId` ist Pflicht:

| Eigenschaft | Bedeutung |
| --- | --- |
| `OrderId` | Die von der Bank vergebene Auftrags-ID aus HVU/HVZ. **Pflicht.** |
| `PartnerId` | Kunde des Einreichers; Default ist der eigene `PartnerID`. |
| `OrderType` | Klassischer Auftragstyp des referenzierten Auftrags (H003/H004); dient auf H005 der BTF-Auflösung. |
| `Btf` | H005-`Service` des referenzierten Auftrags; wird sonst aus `OrderType` abgeleitet. |
| `FileFormat` | Nur H004, wenn der Auftrag als generischer `FUL` eingereicht wurde. |

Fehlt die Referenz bei HVE/HVS (Upload) bzw. HVD/HVT (Download), schlägt der Aufruf **clientseitig** mit
`EbicsConfigurationException` fehl — mit einer Meldung, die benennt was fehlt, statt mit dem generischen
`091121` der Bank.

## Versions-Dispatch

| Aspekt | H003 | H004 | H005 |
| --- | --- | --- | --- |
| Park-Trigger | `OrderAttribute=OZHNN` | `OrderAttribute=OZHNN` | `BTUOrderParams/SignatureFlag` |
| Auftragstyp im Header | `OrderType` | `OrderType` | `AdminOrderType` (**kein** BTU/BTD) |
| Order-Params | `Hve`/`Hvs`/`Hvd`/`HvtOrderParamsType` mit `PartnerID`/`OrderType`/`OrderID` | dito, zusätzlich `FileFormat` | dito, aber `Service` (BTF) statt `OrderType` |

## Returncodes

| Code | Bedeutung |
| --- | --- |
| `000000` | HVE/HVS angenommen |
| `011000` | HVU/HVZ/HVD/HVT ausgeliefert (Download-Postprocessing) |
| `090003` | Teilnehmer darf den zugrundeliegenden Auftrag nicht zeichnen |
| `090004` | Doppelunterschrift bzw. bereits vollständig gezeichnet |
| `090005` | HVD/HVT: keine Daten zur angegebenen `OrderID` |
| `091121` | `EBICS_INVALID_ORDER_IDENTIFIER` — unbekannte `OrderID` |

## Spec-Vorbehalte

- **Nur die `OrderID` wird serverseitig ausgewertet.** Die übrigen Felder der `VeuOrderReference`
  (PartnerID, OrderType/Service, FileFormat) werden schema-konform emittiert, aber der Emulator
  schlüsselt seinen VEU-Speicher allein über die `OrderID`. Gegen eine reale Bank ungeprüft.
- **Der Park-Trigger ist Design-Intent.** Dass `OZHNN` bzw. `SignatureFlag` die maßgeblichen Signale sind,
  ist nicht gegen die offiziellen Annexe verifiziert (Schemas proprietär,
  [ADR-0003](../adr/0003-umgang-mit-proprietaeren-schemas.md)).
- **Die HVE-Signatur wird nicht geprüft.** `HveUploadRequest.SignaturePayload` trägt per Default einen
  minimalen Platzhalter; der Emulator protokolliert *dass* ein Berechtigter gezeichnet hat (ADR-0020).
- **HVT ist auftrags-summarisch** — keine ISO-20022-Einzeltransaktions-Zerlegung.
- **Freigabe nach Anzahl**, nicht nach kontobezogenen Unterschriftsregeln.

## Tests

`tests/EBICO.Tests/E2E/VeuE2ETests.cs` — echter Round-Trip Connector ↔ Server je H003/H004/H005:

- Auftrag parken und in HVU wiederfinden (inkl. `1/2` Unterschriften des Einreichers),
- Freigabe durch einen **zweiten** Teilnehmer (`EbicsE2EHarness.AddCoSignerAsync`),
- Doppelunterschrift des Einreichers wird abgelehnt,
- Storno via HVS,
- HVD löst die referenzierte `OrderID` auf — und findet zu einer fremden ID nichts (`090005`),
- unbekannte `OrderID` bei HVE → `091121`,
- fehlende Referenz → clientseitige `EbicsConfigurationException` ohne Round-Trip.

Dazu `UploadValidationTests` für den H005-`AdminOrderType`-Pfad.

## Verwandte Doku

- [VEU-Orders (Server)](../server/veu-orders.md) — die serverseitige Umsetzung und der Zustandsautomat
- [Upload-API](upload.md) · [Download-API](download.md) — die Familien, in die sich VEU einreiht
- [Order-/BTF-Abdeckungsmatrix](../server/order-coverage-matrix.md) — Server- und Client-Verfügbarkeit
- [ADR-0030](../adr/0030-defaults-und-clientseitige-veu-anbindung.md) · [ADR-0020](../adr/0020-veu-orders.md)
