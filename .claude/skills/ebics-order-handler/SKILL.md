---
name: ebics-order-handler
description: >-
  Anleitung zum Anlegen oder Ändern einer serverseitigen EBICS-Auftragsart in EBICO.Server.
  Verwenden bei neuem/geändertem Order-Handler (Schlüsselmanagement wie INI/HIA/HPB/HCA/HCS/SPR/HSA)
  ODER neuem Upload-/Download-Processor für Business-Orders (z. B. CCT/CDD/CDB/CIP, STA/VMK/C53/C52/C54,
  HTD/HKD/HAA/HPD/HAC/PTK, HVU/HVZ/HVD/HVT/HVE/HVS). Deckt DI-Registrierung, Multi-Version-Dispatch,
  BTF-Auflösung, Berechtigung und die Definition of Done (Tests + Doku + ADR + Coverage-Matrix) ab.
---

# EBICS Order-Handler / Processor anlegen

Zwei getrennte Erweiterungspunkte — zuerst entscheiden, welcher passt:

- **Order-Handler** (`IEbicsOrderHandler`): die *handle*-Stufe der Pipeline für **Schlüssel-/
  Verwaltungs-Orders**, die direkt in einer Antwort münden (INI, HIA, HPB, HCA, HCS, SPR, HSA).
- **Upload-/Download-Processor** (`IUploadOrderProcessor` / `IDownloadOrderProcessor`): die
  order-typspezifische Verarbeitung **innerhalb der Transaction Engine** für **Business-Orders**
  (Zahlungsverkehr, Kontoauszüge, Status-/Protokoll-, VEU-Orders).

Immer zuerst `docs/server/order-coverage-matrix.md` lesen (Source of Truth: welche OrderTypes
je Version schon existieren und wo die Lücken sind).

## Variante A — Order-Handler (Schlüsselmanagement)

Interface: `src/EBICO.Server/Pipeline/IEbicsOrderHandler.cs`
- `EbicsVersion Version` · `string OrderType` · `Task<EbicsOrderResult> HandleAsync(EbicsRequestContext, CancellationToken)`.
- `EbicsOrderResult(EbicsReturnCode ReturnCode, EbicsKeyManagementPayload? Payload = null)` — `Payload`
  nur bei erfolgreichem Download-Key-Order (HPB), sonst `null`.

Muster (siehe INI/HIA/HPB als Vorlage):
1. Base-Klasse `<Xxx>OrderHandlerBase` in `src/EBICO.Server/Handlers/` — versionsagnostischer Fluss,
   Zustandsübergänge, Store-Zugriffe, Returncode-Logik.
2. Je Version eine Subklasse `H003<Xxx>OrderHandler`, `H004<Xxx>OrderHandler`, `H005<Xxx>OrderHandler` —
   nur das Versionsspezifische (H003/H004: `RSAKeyValue`; H005: X.509). HSA existiert nur H003/H004.
3. Registrieren in `src/EBICO.Server/DependencyInjection/EbicoServerServiceCollectionExtensions.cs` mit
   **`services.AddSingleton<IEbicsOrderHandler, H00xXxxOrderHandler>()`** — je Version eine Zeile.
   NICHT `TryAdd`: `EbicsOrderHandlerResolver` konsumiert das ganze `IEnumerable<IEbicsOrderHandler>`
   und matcht nach `(Version, OrderType)`.

## Variante B — Upload-/Download-Processor (Business-Orders)

Interfaces: `src/EBICO.Server/Orders/IUploadOrderProcessor.cs`, `IDownloadOrderProcessor.cs`
- `bool CanProcess(string? effectiveOrderType)` + `ProcessAsync(...)`. Die Engine ruft den **ersten**
  Processor, dessen `CanProcess` matcht.
- Vorlagen: `SepaPaymentUploadProcessor`, `VeuSignatureUploadProcessor` (Upload);
  `StatementDownloadProcessor`, `SubscriberInfoDownloadProcessor`, `CustomerProtocolDownloadProcessor`,
  `VeuOverviewDownloadProcessor` (Download).

Registrieren ebenfalls mit **`AddSingleton`** (nicht `TryAdd`) in `AddEbicoServer`, damit die Defaults
koexistieren und ein Aufrufer eigene Processoren davor hängen kann.

## BTF/OrderType-Auflösung & Berechtigung

- Die generischen Träger (H005 `BTU`/`BTD`+BTF · H003/H004 direkter Code · H003/H004 `FUL`/`FDL`+
  `FileFormat`) werden über `BtfOrderTypeCatalog.ResolveUploadOrderType` /
  `ResolveDownloadOrderType` (`src/EBICO.Core/Btf/BtfOrderTypeCatalog.cs`) auf den effektiven
  klassischen Code (`EffectiveOrderType`, z. B. `CCT`) abgebildet. Neue Order ⇒ Katalogeintrag ergänzen.
- Strikte Berechtigung: `Subscriber.HasPermissionFor` → bei Fehlschlag Returncode `090003`
  (`EBICS_AUTHORISATION_ORDER_TYPE_FAILED`). Returncodes zentral in `EBICO.Core.ReturnCodes`.

## Definition of Done (siehe Skill `ebics-feature-workflow`)

- **Tests:** je Version + Happy/Negativ. Handler → `tests/EBICO.Tests/Server/<Xxx>OrderHandlerTests.cs`;
  Business-Orders → passender Ordner unter `tests/EBICO.Tests/{Core,Server}`.
- **Doku:** neue Seite `docs/server/<name>.md` **und** Eintrag in `docs/index.md`.
- **Coverage-Matrix:** `docs/server/order-coverage-matrix.md` ergänzen — sonst schlägt der Guard-Test
  `OrderCoverageMatrixTests` fehl.
- **ADR:** neue Entscheidung als `docs/adr/NNNN-<kebab-titel>.md` (nächste freie Nummer) + im ADR-Index.
- **Spec-Vorbehalte** im Doku-/Test-Text explizit machen (ES/A00x ungeprüft, unsignierte Antwort etc.).

## Quellen

- Code: `src/EBICO.Server/{Handlers,Orders,Pipeline,Transactions,DependencyInjection}`, `src/EBICO.Core/Btf`.
- Doku: `docs/server/ini.md`, `docs/server/hia.md`, `docs/server/hpb.md`, `docs/server/hca-hcs-spr-hsa.md`,
  `docs/server/payment-orders.md`, `docs/server/statement-orders.md`, `docs/server/status-protocol-orders.md`,
  `docs/server/veu-orders.md`, `docs/server/btf-framework.md`, `docs/server/order-coverage-matrix.md`.
- ADRs: 0012 (Returncodes), 0016 (BTF/Berechtigung), 0017 (Zahlungsverkehr), 0018 (Kontoauszüge),
  0019 (Status/Protokoll), 0020 (VEU).
