# Server: Status- & Protokoll-Orders (HAC/HAA/HTD/HKD/HPD/PTK)

> Umsetzung von **Issue #41** (Milestone M5 — Server: Orders & BTF). Diese Seite beschreibt die
> **administrativen/technischen Download-Orders** auf der [Download-Transaktion](download-transaction.md):
> HTD/HKD/HAA/HPD werden aus den Stammdaten erzeugt, HAC/PTK sind reine Projektionen über den
> [Ereignisspeicher `IEventLog`](event-log.md).
>
> Bewusst **enthalten**: die sechs Order-Typen — **HTD** (Kunden-/Teilnehmerdaten des Teilnehmers), **HKD**
> (Kundendaten inkl. aller Teilnehmer), **HAA** (verfügbare Download-Order-Typen), **HPD** (Bankparameter),
> **HAC** (Customer Protocol, XML) und **PTK** (Customer Protocol, Text); die **Domänen-Erweiterung**
> (`Address`, `BankAccount`, `Partner.Address`/`Partner.Accounts`, `Bank.Url`, `Subscriber.Name`) inkl.
> Admin-API; die versionsbewussten Core-Builder (`SubscriberInfoContentBuilder`, `HacProtocolBuilder`,
> `PtkProtocolBuilder`); zwei pluggbare `IDownloadOrderProcessor` (`SubscriberInfoDownloadProcessor`,
> `CustomerProtocolDownloadProcessor`); die **strikte Berechtigungsprüfung** (wie bei den BTF-Orders).
> Bewusst **noch nicht**: das **wire-exakte** HAC-Format (camt.086/pain.002 — proprietär/kein Schema, hier
> eine plausible Eigen-Projektion); nicht modellierte Bindings-Felder (Order-/Transfer-Format, Betragslimits,
> Autorisierungsstufe, X.509-Parameter, Konto-Nutzungsrestriktionen); die **X002-Signatur** der Antwort (M4);
> die verteilte EU (HVE/HVD/…, [#42](../ticket-overview.md)).

## Zweck

Ein EBICS-Client braucht nach dem Onboarding (INI/HIA/HPB) die **Stamm- und Statusdaten** der Bank: welche
Kunden-/Teilnehmerdaten und Konten hinterlegt sind (HTD/HKD), welche Order-Typen abrufbar sind (HAA), welche
Bankparameter gelten (HPD) und was mit seinen Aufträgen passiert ist (HAC/PTK, „Customer Protocol"). Alle
sechs sind bank→client-**Downloads**, laufen also über die vorhandene
[Download-Transaktion](download-transaction.md) — aber im Gegensatz zu den Kontoauszügen ([#40](statement-orders.md))
bleiben sie in H005 klassische **AdminOrderTypes** (kein BTF-Service, siehe [BTF-Framework](btf-framework.md)).
#41 hängt an der Generate-on-Demand-Stelle der Engine zwei Inhaltsquellen ein: die **Stammdaten**
(`IMasterDataManager`) für HTD/HKD/HAA/HPD und den **Ereignisspeicher** für HAC/PTK.

## Einreichungs-Konventionen & Routing

Die Order-Codes werden **direkt** eingereicht (kein `FDL`/`BTD`, kein FileFormat, kein BTF-Service);
`BtfOrderTypeCatalog.ResolveDownloadOrderType(orderType, null, null)` reicht den rohen Code durch:

| Version | Konvention | Beispiel | Auflösung |
| --- | --- | --- | --- |
| H005 | `AdminOrderType` **direkt** | `AdminOrderType=HTD` | → `HTD` |
| H003/H004 | klassischer `OrderType` **direkt** | `OrderType=HTD` | → `HTD` |

Die Routing-Erkennung `DownloadTransactionEngine.IsDownloadOrderType` kennt neben `FDL`/`BTD` und den
Statement-Codes jetzt auch die Status-/Protokoll-Codes (`StatusProtocolOrderTypes.IsStatusProtocolOrderType`:
HTD/HKD/HAA/HPD/HAC/PTK). Der Code wird — wie bei allen Downloads — **vor** der Berechtigungsprüfung
aufgelöst und als Queue-/Generierungs-Schlüssel weitergereicht.

Die Erzeugung ist auf **mehrere** `IDownloadOrderProcessor` verteilt: die Engine nimmt jetzt
`IEnumerable<IDownloadOrderProcessor>` und wählt den ersten passenden `CanProcess`. Registriert sind
`StatementDownloadProcessor` (#40), `SubscriberInfoDownloadProcessor` (HTD/HKD/HAA/HPD) und
`CustomerProtocolDownloadProcessor` (HAC/PTK).

## Ablauf

Auflösung, Autorisierung und Bereitstellung passieren in der **Initialisation**; Transfer/Receipt arbeiten
unverändert auf der erzeugten Payload (siehe [Download-Transaktion](download-transaction.md)):

| Schritt | Aktion |
| --- | --- |
| 1. Auflösen | effektiven Order-Code = roher Admin-/Order-Code; für HAC/PTK optionalen `DateRange` extrahieren |
| 2. Autorisieren | `Subscriber.HasPermissionFor(code)` — sonst `090003` (Berechtigung erforderlich, kein Auto-Grant) |
| 3a. Entnehmen | Queue nach dem Code probieren (Admin-seedbare Roh-Payload hat Vorrang) |
| 3b. Generieren | HTD/HKD/HAA/HPD aus `IMasterDataManager`; HAC/PTK aus `IEventLog` (kundensichtbar, je Kunde) |
| 4. Senden | Komprimieren (`EbicsCompression`) → E002-Verschlüsseln → Segmentieren → Segment 1 + `NumSegments` |

Die erzeugte Payload ist **Klartext** (XML für HTD/HKD/HAA/HPD/HAC, Text für PTK, **kein** ZIP); die
Verschlüsselung/Segmentierung macht ausschließlich die Engine. Der HTD/HKD/HAA/HPD-Abruf schreibt ein
kundensichtbares `OrderAccepted`-Ereignis; der **HAC/PTK-Abruf** schreibt nur ein `Internal`-Ereignis (kein
zusätzliches kundensichtbares `OrderAccepted`). Die `DownloadStarted`/`DownloadCompleted`-Lifecycle-Ereignisse
der Transaktion bleiben — wie bei jedem Download — kundensichtbar; ein Protokoll-Abruf ist somit in späteren
Protokollen selbst sichtbar.

### Stammdaten-Quelle

HTD/HKD füllen `PartnerInfo` (Adresse, Bank-Info, Konten, Order-Info) und `UserInfo` (UserID/Name,
Berechtigungen) aus dem erweiterten Domänenmodell: `Partner.Address`/`Partner.Accounts`, `Subscriber.Name`
und die Teilnehmer-Permissions. HPD zieht `AccessParams` (URL/Institute/HostID) aus `Bank`
(`Url`/`Name`/`HostId`) und `ProtocolParams/Version` aus `Bank.SupportedVersions` (+ feste Krypto-Versionen
X002/E002/A005/A006). HAA listet die download-fähigen Order-Typen des Teilnehmers.

### Beispiel — HTD (H005, gekürzt)

```xml
<HTDResponseOrderData xmlns="urn:org:ebics:H005">
  <PartnerInfo>
    <AddressInfo><Name>Acme GmbH</Name><City>Berlin</City><Country>DE</Country></AddressInfo>
    <BankInfo><HostID>EBICOHOST</HostID></BankInfo>
    <AccountInfo ID="ACC1" Currency="EUR" Description="Main account">
      <AccountNumber international="true">DE89370400440532013000</AccountNumber>
      <BankCode international="true">COBADEFFXXX</BankCode>
    </AccountInfo>
    <OrderInfo><Service><ServiceName>EOP</ServiceName><MsgName>camt.053</MsgName></Service> … </OrderInfo>
  </PartnerInfo>
  <UserInfo>
    <UserID Status="5">USER01</UserID><Name>Alice</Name>
    <Permission><AdminOrderType>HTD</AdminOrderType></Permission>
  </UserInfo>
</HTDResponseOrderData>
```

In H003/H004 tragen `OrderInfo`/`Permission` statt `AdminOrderType`/`Service` die klassischen
`OrderType`/`OrderTypes`, und HAA listet `OrderTypes` (Codes) statt `Service` (BTF).

### Beispiel — HAC (Customer Protocol, Eigen-Projektion, gekürzt)

```xml
<HACResponseOrderData xmlns="urn:org:ebics:H005">
  <ProtocolEntry sequence="7" timestamp="2026-07-15T10:00:00Z" severity="Info">
    <OrderType>CCT</OrderType>
    <ReturnCode symbolic="EBICS_OK">000000</ReturnCode>
    <Message>Download started (1 segment(s), order type HTD).</Message>
  </ProtocolEntry>
</HACResponseOrderData>
```

PTK rendert dieselbe Projektion als Klartext, eine Zeile pro Ereignis
(`2026-07-15T10:00:00Z [Info] CCT 000000 (EBICS_OK): …`).

## Returncodes & Fehlerfälle

| Situation | Returncode | Ablage |
| --- | --- | --- |
| Erfolg (Segment 1 geliefert) | `000000` EBICS_OK | Header + Body |
| keine Berechtigung für den Order-Typ | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | Body |
| Teilnehmer nicht `Ready`/unbekannt | `091002` EBICS_INVALID_USER_OR_USER_STATE | Body |
| Stammdaten nicht auffindbar (Bank/Partner) | `090005` EBICS_NO_DOWNLOAD_DATA_AVAILABLE | Body |

Die übrigen Transaktions-/Segment-Codes stammen unverändert aus der
[Download-Transaktion](download-transaction.md).

### ⚠️ Spec-Vorbehalte

- **HAC/PTK-Format.** EBICS definiert HAC über ein proprietäres, versionsabhängiges Schema
  (camt.086/pain.002-Ableitung), das nicht im Repo liegt (Lizenz). HAC ist hier eine strukturell plausible,
  selbstbeschreibende **Eigen-Projektion** der Ereignisse (`ProtocolEntry` je Event), PTK eine lesbare
  Textform — beide gegen die offiziellen Annexe ungeprüft.
- **Versionsspezifische Feldabbildung.** HTD/HKD/HAA/HPD werden je Version in die generierten Bindings
  gefüllt; nicht im Domänenmodell geführte Felder (Order-/Transfer-Format, `MaxAmount`, `AuthorisationLevel`,
  `X509Data`, Konto-`UsageOrderTypes`) bleiben leer/ausgelassen.
- **User-`Status`.** Der EBICS-User-Status (`UserID/@Status`) ist heuristisch aus dem Lebenszyklus
  abgeleitet (`Ready`→5, `Initialized`→2, sonst 1).
- **HAA-Umfang.** HAA listet die download-fähigen (BTF-)Order-Typen des Teilnehmers (STA/C5x); rein
  administrative Downloads werden nicht als HAA-Service geführt.
- **Unsignierte Antwort.** X002 weiterhin zurückgestellt (M4), wie bei der Download-Transaktion.

## EBICS-Versionsbezug

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Auftragsidentität | `OrderType` direkt (HTD/HKD/HAA/HPD/HAC/PTK) | `AdminOrderType` direkt |
| HTD/HKD `OrderInfo` | `OrderType` (+ H004 `FileFormat`) / `TransferType` | `AdminOrderType` **oder** `Service` (BTF) |
| HTD/HKD `Permission` | `OrderTypes` (Liste) | `AdminOrderType` **oder** `Service` |
| HAA | `OrderTypes` (Codes) | `Service` (BTF `RestrictedServiceType`) |
| HPD `ProtocolParams` | inkl. `X509Data` (ausgelassen) | ohne `X509Data` |
| HAC-Namespace | `http://www.ebics.org/H003` (H003) · `urn:org:ebics:H004` | `urn:org:ebics:H005` |
| PTK | vorhanden (Legacy) | durch HAC ersetzt |

## Tests

`tests/EBICO.Tests/` (xUnit v3 + AwesomeAssertions; keine proprietären Fixtures):

- `Core/Administrative/StatusProtocolOrderTypesTests` — Klassifizierung der sechs Codes.
- `Core/Administrative/SubscriberInfoContentBuilderTests` — HTD über H003/H004/H005 (String-Präsenz) +
  H005-Round-Trip (PartnerInfo/AccountInfo/OrderInfo/UserInfo), HKD (alle Teilnehmer), HAA (H005 `Service`
  vs. H004 `OrderTypes`), HPD (AccessParams/ProtocolParams, H005 + H003).
- `Core/Administrative/CustomerProtocolBuilderTests` — HAC-Namespace/Einträge (H005 + H003-Legacy), leeres
  Protokoll, PTK-Zeilen.
- `Domain/SubscriberInfoDomainTests` — `Partner.Address`/`Accounts`, `Bank.Url`, `Subscriber.Name` (inkl.
  Erhalt über `Transition`/`WithPermission(s)`/`WithoutPermissionsFor`), `BankAccount`-Default-Währung.
- `Server/StatusProtocolDownloadTests` — **end-to-end** durch die Pipeline: HTD (H003/H004/H005), HKD, HAA
  (H005/H004), HPD, HAC/PTK (Projektion nach vorherigem Download), fehlende Berechtigung → `090003`.
- `Server/AdminApiIntegrationTests` — Round-Trip Bank-`Url`, Partner-Adresse/Konten, Subscriber-`Name`.

## Verwandte Doku

- [Download-Transaktion (Initialisation + Transfer + Receipt)](download-transaction.md) — die Sendemaschine, an der #41 andockt
- [Ereignis-/Protokollspeicher (`IEventLog`)](event-log.md) — Quelle der HAC/PTK-Projektion (kundensichtbar, je Kunde)
- [Stammdatenverwaltung](master-data.md) — Banken/Partner/Teilnehmer inkl. Adresse/Konten/Name/URL, Berechtigungen, Admin-API
- [BTF-Framework (H005)](btf-framework.md) — Admin- vs. BTF-Order-Typen, Berechtigungsprüfung
- [Download-Orders: Kontoauszüge & Reports](statement-orders.md) — das Schwester-Feature (#40) derselben Engine
- [ADR-0019 (Status- & Protokoll-Orders)](../adr/0019-status-protokoll-orders.md) — Domänen-Erweiterung, HAC als IEventLog-Projektion
