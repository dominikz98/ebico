# Server: BTF-Framework (H005)

> Umsetzung von **Issue #38** (Milestone M5 — Server: Orders & BTF). Diese Seite beschreibt das
> **Fundament** für die konkreten Order-Implementierungen (#39–#42) und die Abdeckungsmatrix (#43):
> das typisierte **Business-Transaction-Format**-Modell (H005), das **Mapping BTF ↔ klassische
> OrderTypes** (H004) und die **Berechtigungsprüfung pro BTF** in den Transaction-Engines.
>
> Bewusst **enthalten**: das Wert-Objekt `BusinessTransactionFormat` (`EBICO.Core.Btf`) als
> hand­geschriebene Projektion des generierten `ServiceType`-Bindings; der statische
> `BtfOrderTypeCatalog` mit einem repräsentativen Best-Effort-Seed; die Extraktion des BTF-Service aus
> `BTUOrderParams`/`BTDOrderParams` in der Pipeline; die **strikte** Autorisierung im Upload-/Download-Init
> (`EBICS_AUTHORISATION_ORDER_TYPE_FAILED`, 090003) über die vorhandenen `SubscriberPermission`s.
> Bewusst **noch nicht**: die konkreten Upload-/Download-Orders (CCT/CDD/STA/C5x, [#39](../ticket-overview.md)/#40),
> Status-/Protokoll-Orders (HAC/HTD/…, #41), die verteilte Unterschrift (HVx, #42); die vollständige
> External Code List; die Auswertung von `SignatureFlag` (ES-Pflicht je BTF) und von
> `FULOrderParams`/`FDLOrderParams`-`FileFormat` (H004) → BTF.

## Zweck

In **EBICS 3.0 (H005)** wird die klassische, dreistellige Auftragsart (H003/H004, z. B. `CCT`, `STA`)
durch die generischen Admin-Auftragsarten **`BTU`** (Upload) und **`BTD`** (Download) plus ein
**Business-Transaction-Format (BTF)** ersetzt. Das BTF beschreibt fachlich, *was* übertragen wird, und
steckt im `BTUOrderParams`/`BTDOrderParams`-Element (`Service`). Bis #38 behandelte der Server den
Auftragstyp durchgängig als **freien String** und wertete bei H005 nur den `AdminOrderType`
(`"BTU"`/`"BTD"`) aus — der eigentliche BTF-Service blieb unbeachtet, und Berechtigungen wurden nie
erzwungen. Dieses Framework schließt die Lücke.

## BTF-Parameter-Modell

`EBICO.Core.Btf.BusinessTransactionFormat` (ein `readonly record struct`, [ADR-0007](../adr/0007-domaenen-value-objects-record-struct.md))
bildet die BTF-Parameter typisiert ab:

| Property | Herkunft (`ServiceType`) | Bedeutung |
| --- | --- | --- |
| `Service` (Pflicht) | `ServiceName` | Service-Code (z. B. `SCT`, `SDD`, `EOP`) |
| `Option` | `ServiceOption` | Zusatzoption (z. B. `COR`, `B2B`) |
| `Scope` | `Scope` | Geltungsbereich (ISO-Land/Issuer) |
| `Container` | `Container` (Flag) | Container-Kennung `SVC`/`XML`/`ZIP` |
| `MessageName` | `MsgName` (Value) | Meldungsname (z. B. `pain.001`, `camt.053`, `mt940`) |
| `MessageVariant` | `MsgName@variant` | ISO-20022-Variante |
| `MessageVersion` | `MsgName@version` | ISO-20022-Version |
| `MessageFormat` | `MsgName@format` | Kodierung (z. B. `XML`) |

Konvertierung zwischen Modell und generiertem Binding: `FromSchema(ServiceType)`,
`TryFromBtfParams(BtfParamsTyp)`, `ToServiceType()`/`ToRestrictedServiceType()`. `CanonicalKey` liefert
einen deterministischen Schlüssel (z. B. `"SCT:pain.001:COR"`) für Logging und als Fallback-Auth-Key.

## BTF ↔ OrderType-Mapping

`EBICO.Core.Btf.BtfOrderTypeCatalog` ist die statische Äquivalenztabelle klassischer OrderType ↔ BTF.
Sie trägt einen **repräsentativen Best-Effort-Seed** der gängigen Zahlungs- und Kontoauszugs-Orders; die
konkreten Orders (#39–#42) erweitern sie, #43 dokumentiert das Ergebnis als Abdeckungsmatrix.

| OrderType | Richtung | Service | Option | Container | MsgName | Beschreibung |
| --- | --- | --- | --- | --- | --- | --- |
| `CCT` | Upload | `SCT` | – | – | `pain.001` | SEPA Credit Transfer |
| `CIP` | Upload | `SCT` | `INST` | – | `pain.001` | SEPA Instant Credit Transfer |
| `CDD` | Upload | `SDD` | `COR` | – | `pain.008` | SEPA Direct Debit (CORE) |
| `CDB` | Upload | `SDD` | `B2B` | – | `pain.008` | SEPA Direct Debit (B2B) |
| `STA` | Download | `EOP` | – | `ZIP` | `mt940` | Kontoauszug (SWIFT MT940) |
| `C53` | Download | `EOP` | – | `ZIP` | `camt.053` | Bank-to-Customer Statement |
| `C52` | Download | `STM` | – | `ZIP` | `camt.052` | Bank-to-Customer Account Report |
| `C54` | Download | `EOP` | – | `ZIP` | `camt.054` | Debit/Credit Notification |

- `TryGetBtf(orderType)` — klassischer Code → BTF.
- `TryGetOrderType(btf)` — BTF → Code (Match auf `Service` + `Option` + `MessageName`-Familie; eine
  gesäte `camt.053` matcht auch ein eingehendes `camt.053.001.08`).
- `ResolveOrderType(adminOrderType, btf)` — **effektiver Auth-Schlüssel**: BTF vorhanden → gemappter
  Code (sonst `CanonicalKey`); kein BTF → `adminOrderType` (H003/H004: `FUL`/`FDL`; H005 ohne BTF: `BTU`/`BTD`).

## Berechtigungsprüfung pro BTF

Die Prüfung setzt auf dem bestehenden Berechtigungsmodell auf ([Stammdaten](master-data.md),
[Domänenmodell](../protocol/domain-model.md)): `Subscriber` bündelt `SubscriberPermission`s (OrderType ×
`SignatureClass`). Neu ist die Gate-Methode `Subscriber.HasPermissionFor(orderType)` (hält *irgendeine*
Berechtigung für den Auftragstyp — im Gegensatz zu `CanAuthorize`, das eine bankfachliche E/A/B-Klasse
verlangt).

Ablauf im Upload-/Download-Init (`UploadTransactionEngine.BeginUploadAsync` /
`DownloadTransactionEngine.BeginDownloadAsync`), **nach** dem `Ready`-Check und — beim Download —
**vor** dem Entnehmen der Daten:

1. Pipeline extrahiert den BTF (`BTUOrderParams`/`BTDOrderParams` → `Service`) in `EbicsRequestContext.Btf`.
2. `effectiveOrderType = BtfOrderTypeCatalog.ResolveOrderType(context.OrderType, context.Btf)`.
3. `subscriber.HasPermissionFor(effectiveOrderType)` → sonst **`090003`**.

**Enforcement ist strikt** (siehe [ADR-0016](../adr/0016-btf-framework-und-berechtigung.md)): eine
`Ready`-Teilnehmerin **muss** eine passende Berechtigung halten; ohne Berechtigung wird der Auftrag mit
`090003` abgewiesen (kein „leere Menge = alles erlaubt").

### Beispiel: H005-BTU mit BTF (`BTUOrderParams`)

```xml
<OrderDetails>
  <AdminOrderType>BTU</AdminOrderType>
  <BTUOrderParams>
    <Service>
      <ServiceName>SCT</ServiceName>
      <MsgName>pain.001</MsgName>
    </Service>
  </BTUOrderParams>
</OrderDetails>
```

Dieser BTF (`SCT`/`pain.001`) wird auf den klassischen OrderType **`CCT`** gemappt; die Teilnehmerin
benötigt eine `CCT`-Berechtigung.

## Returncodes & Fehlerfälle

| Situation | Returncode | Ablage |
| --- | --- | --- |
| Autorisiert (Berechtigung vorhanden) | (Init läuft weiter, i. d. R. `000000`) | – |
| Keine passende Berechtigung für den (aufgelösten) Auftragstyp | `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED | Body |

Der Code `090003` existierte bereits im [Returncode-Katalog](../protocol/return-codes.md), wurde vor #38
aber nie ausgelöst. Alle Fälle werden mit **HTTP 200** und dem Returncode im `ebicsResponse`
beantwortet.

### ⚠️ Spec-Vorbehalte

- **Best-Effort-Mapping.** Die maßgebliche EBICS *BTF-Mapping / External Code List* ist proprietär
  (EBICS SC) und wird **nicht** ins Repo committet ([Lizenz](../legal/ebics-licensing.md)). Der
  Seed folgt der öffentlichen Liste nach bestem Wissen; die exakten Service-/Option-/MsgName-Codes
  werden mit den konkreten Orders (#39–#43) gegen die offizielle Liste verifiziert.
- **Container-Wert nicht round-trip-fähig.** Der SVC/XML/ZIP-Wert liegt im generierten Binding auf
  einem untypisierten Attribut des `Container`-Flags; das Modell liest ihn best-effort, `ToServiceType`
  schreibt nur das Vorhandensein des Flags (nicht den Wert). Nachziehen, sobald das Attribut gegen den
  Annex verifiziert ist.
- **Admin-/technische OrderTypes bleiben Admin-OrderTypes.** `HAC`/`HAA`/`HTD`/`HKD`/`HPD`/`PTK` werden
  bewusst **nicht** als BTF-Service modelliert (in H005 weiterhin `AdminOrderType`); sie sind Thema von #41.
- **`FUL`-`FileFormat` → OrderType (Upload, umgesetzt in #39).** Bei H003/H004 steckt die fachliche
  Auftragsart im `FULOrderParams/FileFormat`; `BtfOrderTypeCatalog.TryGetOrderTypeByFileFormat` /
  `ResolveUploadOrderType` bilden die MsgName-Familie (z. B. `pain.001.001.09` → `CCT`) für die
  Upload-Autorisierung/-Verarbeitung ab (siehe [Zahlungsverkehr-Orders](payment-orders.md)). Die
  **Download**-Seite (`FDL`-`FileFormat`) bleibt **[#40](download-transaction.md)** vorbehalten; die
  Option (CORE/B2B) ist aus dem FileFormat allein nicht ableitbar (CDD-Default).
- **`SignatureFlag` (ES-Pflicht je BTF).** Ob ein BTU-Auftrag eine ES fordert, steuert spec-seitig
  `BTUOrderParams/SignatureFlag`; das ist von der reinen OrderType-Berechtigung getrennt und offen
  (vgl. [Upload-Transaktion](upload-transaction.md)).

## EBICS-Versionsbezug

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Auftragstyp | `OrderDetails/OrderType` (z. B. `FUL`/`FDL`, klassischer Code) | `OrderDetails/AdminOrderType` (`BTU`/`BTD`) |
| Fachliche Identität | im OrderType bzw. `FileFormat` | im **BTF** (`BTUOrderParams`/`BTDOrderParams` → `Service`) |
| Autorisierungs-Schlüssel | OrderType-String direkt | BTF → klassischer Code (Katalog), sonst Fallback |

BTF ist rein **H005**; H003/H004 tragen keinen BTF-Service.

## Tests

`tests/EBICO.Tests/Core/Btf/` und `tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Request-XML
aus committeten Core-Bindings, keine proprietären Fixtures):

- `BusinessTransactionFormatTests` — Konstruktion/Validierung, `FromSchema`↔`ToServiceType`-Roundtrip,
  `CanonicalKey`, `TryFromBtfParams`, Wert-Gleichheit.
- `BtfOrderTypeCatalogTests` — Roundtrips je Seed-Eintrag, `TryGetOrderType`-Matching (inkl. MsgName-
  Familie), `ResolveOrderType` (H004-OrderType, H005-mit/ohne-BTF, Unmapped-Fallback).
- `SubscriberTests` — `HasPermissionFor` (jede Signaturklasse zählt).
- `BtfAuthorizationTests` — End-to-end über die Pipeline: H005-BTU mit gemapptem BTF → `Ok` (mit
  passender Berechtigung) bzw. **`090003`** (ohne); H004-`FUL` ohne Berechtigung → `090003`; H005 ohne
  BTF → Fallback auf `BTU`-Berechtigung; Download-BTD analog (`C53`).

## Verwandte Doku

- [Upload-Transaktion](upload-transaction.md) / [Download-Transaktion](download-transaction.md) — die Engines, in denen die Prüfung andockt
- [Stammdatenverwaltung](master-data.md) — `SubscriberPermission`, Grant/Revoke, Admin-API
- [Domänenmodell](../protocol/domain-model.md) — Subscriber-Aggregat, Signaturklassen
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — `090003` EBICS_AUTHORISATION_ORDER_TYPE_FAILED
- [ADR-0016 (BTF-Framework & Berechtigung)](../adr/0016-btf-framework-und-berechtigung.md) — Entscheidungen *strikt* & *Bridge über OrderType-Code*
- [Lizenz & Repo-Policy](../legal/ebics-licensing.md) — proprietäre Schemas/External Code List
