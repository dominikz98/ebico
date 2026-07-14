# 0016 — BTF-Framework & Berechtigungsprüfung

- Status: accepted
- Datum: 2026-07-14

## Kontext

EBICS 3.0 (H005) ersetzt die klassischen dreistelligen Auftragsarten (H003/H004) durch die generischen
Admin-Auftragsarten `BTU`/`BTD` plus ein **Business Transaction Format (BTF)** im
`BTUOrderParams`/`BTDOrderParams`-Element. Bis dahin behandelte der Server den Auftragstyp als freien
String, wertete bei H005 nur den `AdminOrderType` aus und erzwang **keine** Berechtigungen (die Engines
prüften nur `State == Ready`). Issue #38 liefert das Framework für die konkreten Orders (#39–#43):
typisiertes BTF-Modell, BTF↔OrderType-Mapping und Berechtigungsprüfung pro BTF. Dabei waren zwei
Entscheidungen zu treffen: (a) wie streng autorisiert wird und (b) wie BTF-Berechtigungen ausgedrückt
werden.

## Entscheidung

1. **Typisiertes Modell in `EBICO.Core.Btf`.** `BusinessTransactionFormat` (`readonly record struct`,
   [ADR-0007](0007-domaenen-value-objects-record-struct.md)) als handgeschriebene Projektion des
   generierten `ServiceType`-Bindings; die generierten `Schema/H005/*`-Typen werden gemappt, nicht
   editiert ([ADR-0006](0006-generierte-xsd-bindings-committen.md)).

2. **Bridge über OrderType-Code.** Der statische `BtfOrderTypeCatalog` mappt BTF ↔ klassischen Code.
   Die Autorisierung nutzt einen einzigen **effektiven Auftragstyp-Schlüssel**: für H005 wird der BTF
   auf seinen klassischen Code aufgelöst, für H003/H004 der OrderType direkt verwendet.
   `SubscriberPermission.OrderType` bleibt ein String; die Admin-API und der `MasterDataManager` bleiben
   **unverändert**. (Verworfen: ein natives `BusinessTransactionFormat`-Feld in `SubscriberPermission` —
   größere API-/Persistenz-Fläche ohne Mehrwert für den Emulator.)

3. **Striktes Enforcement.** Eine `Ready`-Teilnehmerin muss eine passende Berechtigung halten; sonst
   `EBICS_AUTHORISATION_ORDER_TYPE_FAILED` (090003). Es gibt **kein** „leere Berechtigungsmenge = alles
   erlaubt". (Verworfen: lenient/opt-in — der Emulator soll das echte Bankverhalten abbilden.)

4. **Statische Prüf-Logik, kein neuer DI-Service.** `BtfOrderTypeCatalog` (Core) + `Subscriber.HasPermissionFor`
   (Core) werden inline in den Engines aufgerufen; die Engine-Konstruktoren und die DI-Registrierung
   bleiben unverändert. Die Logik ist als statischer Helper direkt unit-testbar.

## Konsequenzen

- Der Katalog-Seed ist **repräsentativ und best-effort** (die maßgebliche External Code List ist
  proprietär, [ADR-0003](0003-umgang-mit-proprietaeren-schemas.md)); #39–#43 erweitern und verifizieren ihn.
- Bestehende Upload-/Download-Tests, die `Ready`-Teilnehmer ohne Berechtigungen anlegten, wurden migriert
  (passende Berechtigungen geseedet) — Folge des strikten Enforcements.
- Für H005-BTF-only-Services ohne klassischen Code greift der `CanonicalKey` als Fallback-Schlüssel.
- `FUL`/`FDL`-`FileFormat` → BTF und die Auswertung von `SignatureFlag` bleiben späteren Issues
  vorbehalten (siehe [BTF-Framework-Doku](../server/btf-framework.md)).

## Alternativen

- **Natives BTF in `SubscriberPermission`** (statt Bridge) — abgelehnt (s. o.).
- **Lenient/opt-in-Enforcement** — abgelehnt (s. o.).
- **Eigener `IOrderAuthorizationService` via DI** — abgelehnt: unnötige Kopplung/Ctor-Ripple; die
  statische Variante ist ebenso testbar.
