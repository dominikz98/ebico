# 0019 — Status- & Protokoll-Orders (Domänen-Erweiterung, HAC/PTK als IEventLog-Projektion)

- Status: accepted
- Datum: 2026-07-15

## Kontext

Issue #41 (Milestone M5) fordert die **administrativen/technischen Order-Typen**: HTD (Teilnehmerdaten),
HKD (Kundendaten), HAA (verfügbare Order-Typen), HPD (Bankparameter) sowie HAC/PTK (Customer Protocol,
maschinen- bzw. menschenlesbar). Diese bleiben in H005 **AdminOrderTypes** (kein BTF-Service, siehe
[ADR-0016](0016-btf-framework-und-berechtigung.md)) und sind bank→client-**Downloads**.

Vorhanden waren: die generische [Download-Transaktion](../server/download-transaction.md) (#33), das
Generate-on-Demand-Muster `IDownloadOrderProcessor` (#40, [ADR-0018](0018-kontoauszug-download-orders.md))
und der append-only [`IEventLog`](../server/event-log.md) (#69, [ADR-0015](0015-ereignis-protokollspeicher.md)),
der ausdrücklich als Quelle für die HAC-Projektion vorgesehen ist. Ebenfalls vorhanden: generierte Bindings
`HTD/HKD/HAA/HPDResponseOrderData` je Version — **nicht** aber für HAC/PTK (proprietäres/kein Schema).

Zu entscheiden war: (1) woher die Stammdaten für HTD/HKD/HPD stammen, (2) ob diese Orders eine explizite
Berechtigung erfordern, (3) in welchem Format HAC ausgeliefert wird, (4) wie das Routing/die Erzeugung
angebunden wird.

## Entscheidung

1. **Domänenmodell erweitern** statt synthetisch zu befüllen: neue Value-Types `Address` und `BankAccount`
   in `EBICO.Core.Domain`; `Partner` trägt nun `Address?` + `IReadOnlyCollection<BankAccount>`, `Bank` ein
   optionales `Url` (HPD-Zugang, `Institute`=`Name`), `Subscriber` einen optionalen `Name`. Die Admin-API
   (`MapEbicoAdminApi`) und ihre DTOs wurden entsprechend erweitert; `Name` wird durch alle immutablen
   Subscriber-Kopieroperationen (`Transition`/`WithPermission(s)`/`WithoutPermissionsFor`) durchgereicht.

2. **Berechtigung erforderlich**: die Orders laufen unverändert durch das strikte `HasPermissionFor`-Gate
   der Download-Engine (fehlt die Permission → `090003`). Kein Auto-Grant, keine Ausnahme für Admin-Orders —
   konsistent mit den BTF-Orders (ADR-0016). Der Teilnehmer wird über die Admin-API für HTD/HAC/… berechtigt.

3. **HAC als eigene, spec-plausible XML-Projektion** über `IEventLog`
   (`QueryAsync { PartnerId, Visibility = CustomerVisible }`, optional zeitraum-gefiltert): ein
   `HACResponseOrderData` mit je einem `ProtocolEntry` pro kundensichtbarem Ereignis (handgebaut wie die
   camt/pain-Builder). **PTK** rendert dieselbe Projektion als Text. Die Erzeugung selbst wird nur als
   `Internal`-Ereignis geloggt (nicht als zusätzliches kundensichtbares `OrderAccepted`); die
   `DownloadStarted`/`DownloadCompleted`-Lifecycle-Ereignisse der Transaktion bleiben — wie bei jedem
   Download — kundensichtbar, ein Protokoll-Abruf ist also in späteren Protokollen selbst sichtbar.

4. **Anbindung als Download** über zwei neue `IDownloadOrderProcessor`: `SubscriberInfoDownloadProcessor`
   (HTD/HKD/HAA/HPD, aus dem `IMasterDataManager` via `SubscriberInfoContentBuilder`) und
   `CustomerProtocolDownloadProcessor` (HAC/PTK). `DownloadTransactionEngine.IsDownloadOrderType` erkennt die
   Codes zusätzlich (`StatusProtocolOrderTypes`); die Engine nimmt jetzt `IEnumerable<IDownloadOrderProcessor>`
   und wählt den ersten passenden `CanProcess` (statt genau eines Prozessors).

## Konsequenzen

- Der Emulator beantwortet alle sechs Orders über alle drei Versionen; HTD/HKD/HAA/HPD sind durch Round-Trip
  gegen die Bindings testbar, HAC/PTK gegen die projizierten Ereignisse — ohne proprietäre Fixtures.
- Die Umstellung auf mehrere `IDownloadOrderProcessor` ist additiv (der Statement-Processor #40 bleibt
  registriert); Fremd-Prozessoren lassen sich weiterhin via `AddSingleton` ergänzen.
- **Spec-Vorbehalte:** HAC/PTK sind plausible Eigenformate (das offizielle camt.086/pain.002-Layout ist nicht
  verifiziert); die versionsspezifische HTD/HKD/HAA/HPD-Feldabbildung lässt nicht modellierte Felder
  (Order-/Transfer-Format, Betragslimits, Autorisierungsstufe, X.509-Parameter, Konto-Nutzungsrestriktionen)
  aus. Der EBICS-User-`Status` ist heuristisch aus dem Lebenszyklus abgeleitet.

## Alternativen

- **Synthetische Stammdaten** (kein Domänenmodell) — schneller, aber HTD/HKD/HPD blieben inhaltsleer;
  verworfen zugunsten echter, über die Admin-API/Suite pflegbarer Konten/Adressen/Bankparameter.
- **Admin-Orders ohne Berechtigung** (für jeden `Ready`-Teilnehmer) — bequemer, weicht aber vom strikten
  Berechtigungsmodell (ADR-0016) ab; verworfen zugunsten der Konsistenz.
- **HAC an pain.002 anlehnen** (`PainStatusReportBuilder` wiederverwenden) — semantisch nur für Auftragsstatus
  passend, nicht für ein vollständiges Ereignisprotokoll; verworfen zugunsten der eigenen Projektion.
- **Eigener Order-Handler statt Download-Processor** — hätte Verschlüsselung/Segmentierung dupliziert;
  verworfen zugunsten der bestehenden Download-Transaktion.
