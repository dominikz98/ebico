# Server: Order-/BTF-Abdeckungsmatrix

> Umsetzung von **Issue #43** (Milestone M5 — Server: Orders & BTF). Diese Seite fasst die konkreten
> Order-Implementierungen aus #38–#42 zu einer **konsolidierten Abdeckungsmatrix** zusammen: welche
> OrderTypes/BTFs der Emulator in welcher EBICS-Version (H003/H004/H005) mit welchem Status behandelt —
> inklusive markierter, offener Lücken.
>
> Bewusst **enthalten**: eine familienweise Übersicht über die gesamte bisher modellierte Order-Palette
> (Schlüsselmanagement, generische Transportaufträge, Zahlungsverkehr, Kontoauszüge, Status-/Protokoll-,
> VEU-Orders), die H005-BTF-Zuordnung der fachlichen Business-Orders und ein eigener Lücken-Abschnitt.
> Bewusst **noch nicht**: Vollständigkeit gegenüber der proprietären EBICS *BTF-Mapping / External Code
> List* (nicht im Repo, [Lizenz](../legal/ebics-licensing.md)) und die Konformität gegen reale Clients
> (Milestone **M8**).

## Zweck

Die Auftragstypen sind über die Codebasis verteilt (durchgängig freie Strings, gruppiert in statischen
Katalogen — es gibt kein zentrales OrderType-Enum). Diese Seite ist die eine, menschenlesbare
Gesamtübersicht darüber, was der Emulator abdeckt. **Quelle der Wahrheit bleibt der Code**; die Matrix
wird per Guard-Test (`OrderCoverageMatrixTests`, siehe [Tests](#tests)) synchron gehalten, sodass kein im
Code registrierter OrderType still aus der Matrix herausfallen kann.

Die maßgeblichen Kataloge im Code:

- `EBICO.Core.Btf.BtfOrderTypeCatalog` (`All`) — BTF ↔ klassischer Code für die Business-Orders.
- `EBICO.Core.Payments.PaymentOrderTypes`, `EBICO.Core.Statements.StatementOrderTypes`,
  `EBICO.Core.Administrative.StatusProtocolOrderTypes`, `EBICO.Core.Administrative.VeuOrderTypes`.
- Server-Handler-/Engine-Konstanten (`EBICO.Server.Handlers.*OrderHandlerBase`,
  `EBICO.Server.Transactions.{Upload,Download}TransactionEngine`).

## Legende

- ✅ **implementiert & getestet** — vom Emulator behandelt und durch Unit-/Integrationstests abgesichert.
- 🟡 **teilweise / best-effort** — funktional vorhanden, aber mit dokumentierten Spec-Vorbehalten
  (siehe [Offene Lücken](#offene-lücken)).
- ❌ **geplant / offen** — noch nicht implementiert (ggf. nur als Schema-Binding vorhanden).
- `–` **nicht zutreffend** — in dieser Version bzw. für diesen Auftragstyp nicht vorgesehen.

Die Spalten **H003 / H004 / H005** geben an, ob der Auftragstyp in der jeweiligen Protokollversion vom
Emulator unterstützt wird. Die Spalte **BTF (H005)** nennt das H005-Business-Transaction-Format
(`Service` / `MsgName`, ggf. Option) der fachlichen Business-Orders; administrative/technische Orders
bleiben in H005 `AdminOrderType`s und tragen daher `–`.

## Schlüsselmanagement & Onboarding

Klassische Schlüssel-/Onboarding-Auftragsarten. In allen Versionen als klassischer Code bzw. H005-
`AdminOrderType` (kein BTF). Siehe [INI](ini.md), [HIA](hia.md), [HPB](hpb.md),
[HCA/HCS/SPR/HSA](hca-hcs-spr-hsa.md).

| OrderType | Beschreibung | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `INI` | Signaturschlüssel (A00x) senden | – | ✅ | ✅ | ✅ | ✅ |
| `HIA` | Authentifikations- & Verschlüsselungsschlüssel (X00x/E00x) senden | – | ✅ | ✅ | ✅ | ✅ |
| `HPB` | Öffentliche Bankschlüssel abrufen | – | ✅ | ✅ | ✅ | ✅ |
| `HCA` | Schlüsselwechsel Auth/Enc | – | ✅ | ✅ | ✅ | ✅ |
| `HCS` | Schlüsselwechsel Signatur + Auth + Enc | – | ✅ | ✅ | ✅ | ✅ |
| `SPR` | Zugang sperren (Teilnehmer suspendieren) | – | ✅ | ✅ | ✅ | ✅ |
| `HSA` | Schlüsselübermittlung (Legacy-HIA) | – | ✅ | ✅ | ❌ | ✅ |

`HSA` ist in **H005 entfernt** (kein H005-Handler registriert) und daher dort `❌`.

## Generische Transportaufträge (Träger)

Die generischen Träger-Auftragsarten, über die H003/H004 bzw. H005 fachliche Uploads/Downloads
transportieren. Die fachliche Identität steckt bei H003/H004 im `FileFormat`, bei H005 im BTF. Siehe
[Upload-Transaktion](upload-transaction.md) / [Download-Transaktion](download-transaction.md).

| OrderType | Beschreibung | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `FUL` | Generischer Upload (File Upload, mit `FileFormat`) | – | ✅ | ✅ | – | ✅ |
| `FDL` | Generischer Download (File Download, mit `FileFormat`) | – | ✅ | ✅ | – | ✅ |
| `BTU` | Generischer Upload (Business Transaction Upload, trägt BTF) | – | – | – | ✅ | ✅ |
| `BTD` | Generischer Download (Business Transaction Download, trägt BTF) | – | – | – | ✅ | ✅ |

## Zahlungsverkehr — Upload

Fachliche SEPA-Uploads (#39). Einreichung über H005 `BTU`+BTF, H003/H004 `FUL`+`FileFormat` oder direkten
Code. Siehe [Zahlungsverkehr-Orders](payment-orders.md).

| OrderType | Beschreibung | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `CCT` | SEPA Credit Transfer | `SCT` / `pain.001` | ✅ | ✅ | ✅ | ✅ |
| `CIP` | SEPA Instant Credit Transfer | `SCT` `INST` / `pain.001` | ✅ | ✅ | ✅ | ✅ |
| `CDD` | SEPA Direct Debit (CORE) | `SDD` `COR` / `pain.008` | ✅ | ✅ | ✅ | ✅ |
| `CDB` | SEPA Direct Debit (B2B) | `SDD` `B2B` / `pain.008` | ✅ | ✅ | ✅ | ✅ |

## Kontoauszüge & Reports — Download

Serverseitig generierte, synthetische Auszüge/Reports (#40). Anforderung über H005 `BTD`+BTF, H003/H004
`FDL`+`FileFormat` oder direkten Code. Siehe [Kontoauszug-Orders](statement-orders.md).

| OrderType | Beschreibung | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `STA` | Kontoauszug (SWIFT MT940) | `EOP` / `mt940` | ✅ | ✅ | ✅ | ✅ |
| `VMK` | Vormerkposten / Interim (SWIFT MT942) | `STM` / `mt942` | ✅ | ✅ | ✅ | ✅ |
| `C53` | Bank-to-Customer Statement (camt.053) | `EOP` / `camt.053` | ✅ | ✅ | ✅ | ✅ |
| `C52` | Bank-to-Customer Account Report (camt.052) | `STM` / `camt.052` | ✅ | ✅ | ✅ | ✅ |
| `C54` | Debit/Credit Notification (camt.054) | `EOP` / `camt.054` | ✅ | ✅ | ✅ | ✅ |
| `Z53` | Kontoauszug Swiss/ISO-CH (camt.053) | – | – | ❌ | ❌ | ❌ |

`Z53` ist in der Roadmap zu #40 genannt, aber **nicht implementiert** (offen).

## Status- & Protokoll-Orders — Download

Administrative/technische Download-Orders (#41). Bleiben in H005 `AdminOrderType`s (kein BTF). Siehe
[Status- & Protokoll-Orders](status-protocol-orders.md).

| OrderType | Beschreibung | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `HTD` | Teilnehmerdaten (inkl. Adresse/Konten) | – | ✅ | ✅ | ✅ | ✅ |
| `HKD` | Kundendaten (inkl. aller Teilnehmer) | – | ✅ | ✅ | ✅ | ✅ |
| `HAA` | Verfügbare Auftragsarten | – | ✅ | ✅ | ✅ | ✅ |
| `HPD` | Bankparameter | – | ✅ | ✅ | ✅ | ✅ |
| `HAC` | Kundenprotokoll (XML), Projektion über den Event-Log | – | ✅ | ✅ | ✅ | 🟡 |
| `PTK` | Kundenprotokoll (Text), Projektion über den Event-Log | – | ✅ | ✅ | ✅ | 🟡 |

`HAC`/`PTK` sind funktional vorhanden und getestet, aber als Eigen-Projektion statt spec-genauem
camt.086/pain.002 realisiert (siehe [Offene Lücken](#offene-lücken)).

## Verteilte elektronische Unterschrift (VEU / EDS)

Auftragstypen der verteilten elektronischen Unterschrift (#42). Bleiben in H005 `AdminOrderType`s
(kein BTF). Siehe [VEU-Orders](veu-orders.md).

| OrderType | Beschreibung | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `HVU` | Übersicht offener Aufträge (Download) | – | ✅ | ✅ | ✅ | ✅ |
| `HVZ` | Übersicht mit Zusatzdetails (Download) | – | ✅ | ✅ | ✅ | ✅ |
| `HVD` | Status/Detail eines Auftrags (Download) | – | ✅ | ✅ | ✅ | ✅ |
| `HVT` | Transaktionsdetails eines Auftrags (Download) | – | ✅ | ✅ | ✅ | 🟡 |
| `HVE` | Unterschrift hinzufügen (Upload) | – | ✅ | ✅ | ✅ | ✅ |
| `HVS` | Auftrag stornieren/ablehnen (Upload) | – | ✅ | ✅ | ✅ | ✅ |

`HVT` liefert die Detailübersicht auftrags-summarisch (keine ISO-20022-Einzeltransaktions-Zerlegung,
siehe [Offene Lücken](#offene-lücken)).

## Nur Schema-Binding (nicht verdrahtet)

Auftragsarten, für die generierte Bindings existieren, die aber **kein** Handler behandelt.

| OrderType | Beschreibung | BTF (H005) | H003 | H004 | H005 | Status |
| --- | --- | --- | --- | --- | --- | --- |
| `HEV` | Host-EBICS-Versionen abfragen | – | ❌ | ❌ | ❌ | ❌ |
| `H3K` | Kombinierte Schlüsselübermittlung (INI + HIA + Zertifikate) | – | – | ❌ | ❌ | ❌ |

## Offene Lücken

Konsolidierte Liste der bewusst noch nicht abgedeckten Punkte (die zu markierenden „Lücken" aus #43).

- **Z53 (Swiss/ISO-CH-Kontoauszug).** In der Roadmap zu [#40](statement-orders.md) genannt, nicht
  implementiert.
- **PSR / pain.002-Download nicht erreichbar.** Der Zahlungs-Statusreport aus #39 wird intern unter dem
  Platzhalter-Code `PSR` (`EbicoServerOptions.PaymentStatusReportOrderType`) abgelegt, aber **kein**
  OrderType/BTF mappt auf diese Queue — beobachtbar nur über die Admin-API (siehe
  [Zahlungsverkehr-Orders](payment-orders.md)).
- **`SignatureFlag` (ES-Pflicht je BTF) ungeprüft.** Ob ein `BTU`-Auftrag eine ES fordert, wird nicht
  ausgewertet (getrennt von der reinen OrderType-Berechtigung).
- **ES-/X002-Prüfung zurückgestellt (M4).** Payloads werden entschlüsselt, aber nicht authentifiziert;
  Download-Antworten sind unsigniert.
- **camt fest auf `.001.08`.** Das klassische DK-Profil `.02` ist nicht implementiert; keine echte
  ISO-20022-XSD-Validierung (nur strukturell).
- **HAC/PTK Wire-Format.** Eigen-Projektion statt spec-genauem camt.086 (HAC) bzw. pain.002 (PTK).
- **HVT auftrags-summarisch.** Keine ISO-20022-Einzeltransaktions-Zerlegung.
- **BTF-Container nicht round-trip-fähig.** Der `SVC`/`XML`/`ZIP`-Wert liegt best-effort im Binding
  (siehe [BTF-Framework](btf-framework.md)).
- **HEV / H3K nur als Schema-Binding.** Kein Handler, keine Nutzung im Server/Connector.
- **BTF-Katalog ist Best-Effort-Seed.** Nur die gängigen Zahlungs-/Auszugs-Orders sind gegen die
  proprietäre *BTF-Mapping / External Code List* verifiziert; große Teile der EBICS-BTF-Palette sind
  nicht modelliert.
- **Conformance gegen reale Clients / Negativ-Sicherheitsfälle.** Milestone **M8** — **abgeschlossen**
  (Epic #56). Die **E2E-Happy-Paths** Connector ↔ Server (INI/HIA/HPB, CCT, C53 — je H003/H004/H005,
  **#57**) belegen die Konsistenz beider EBICO-Seiten; **#58** ergänzt die serverseitige
  X002-Verifikation + Wire-Tampering-Negativsuite; **#59** prüft gegen **echte Fremd-Clients**
  ([Konformität gegen reale Clients](../development/conformance-real-clients.md)). Die dort aufgedeckten
  Abweichungen (`xsi:type` auf `OrderDetails`, Fehlklassifikation als `061099`, `A006`/PSS auf H004,
  Modulus mit ASN.1-Vorzeichen-Byte) sind mit **#117** behoben
  ([ADR-0029](../adr/0029-interop-fixes-reale-clients.md)); der Vendor-Replay treibt die Onboarding-Kette
  eines realen Clients jetzt bis `Ready` durch. Spec-Konformität gegen die offiziellen Annexe bleibt
  generell nur teilverifiziert (Schemas proprietär).

## EBICS-Versionsbezug

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Auftragstyp | `OrderDetails/OrderType` (klassischer Code, z. B. `CCT`, `STA`, `FUL`, `FDL`) | `OrderDetails/AdminOrderType` (`BTU`/`BTD` für Business-Orders, klassischer Admin-Code sonst) |
| Fachliche Identität | im OrderType bzw. `FULOrderParams`/`FDLOrderParams` `FileFormat` | im **BTF** (`BTUOrderParams`/`BTDOrderParams` → `Service`) |
| Admin-/VEU-Orders | klassischer Code | bewusst weiterhin `AdminOrderType` (kein BTF) |

BTF ist rein **H005**; H003/H004 tragen keinen BTF-Service. Die Auflösung BTF ↔ klassischer Code
übernimmt `BtfOrderTypeCatalog` (Details im [BTF-Framework](btf-framework.md)).

## Tests

`tests/EBICO.Tests/Docs/OrderCoverageMatrixTests.cs` (xUnit v3 + AwesomeAssertions):

- **Vollständigkeits-Guard** — jeder im Code registrierte OrderType (aus `BtfOrderTypeCatalog.All`, den
  vier `*OrderTypes`-Katalogen sowie den Handler-/Engine-Konstanten) **muss** in dieser Matrix
  auftauchen; verhindert stilles Auseinanderdriften von Code und Doku. Umgekehrt darf die Matrix
  zusätzlich geplante/offene Codes (z. B. `Z53`, `HEV`, `H3K`) listen.
- **Struktur-Guard** — die Pflichtabschnitte (`## Legende`, `## Offene Lücken`, `## EBICS-Versionsbezug`)
  sind vorhanden.

Die *fachlichen* Order-Tests je Familie stehen in den jeweiligen Feature-Docs (siehe unten).

## Verwandte Doku

- [BTF-Framework (H005)](btf-framework.md) — BTF-Modell und `BtfOrderTypeCatalog`
- [Zahlungsverkehr-Orders (CCT/CDD/CDB/CIP)](payment-orders.md)
- [Kontoauszug-Orders (STA/VMK/C53/C52/C54)](statement-orders.md)
- [Status- & Protokoll-Orders (HAC/HAA/HTD/HKD/HPD/PTK)](status-protocol-orders.md)
- [VEU-Orders (HVU/HVZ/HVD/HVT/HVE/HVS)](veu-orders.md)
- [INI](ini.md) / [HIA](hia.md) / [HPB](hpb.md) / [HCA/HCS/SPR/HSA](hca-hcs-spr-hsa.md) — Schlüsselmanagement
- [Ticket-Übersicht](../ticket-overview.md) — Milestones und Issues (M5: #38–#43)
- [ADR-0016 (BTF-Framework & Berechtigung)](../adr/0016-btf-framework-und-berechtigung.md)
- [Lizenz & Repo-Policy](../legal/ebics-licensing.md) — proprietäre External Code List
