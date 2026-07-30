---
name: ebics-order-handler
description: >-
  Guide to creating or changing a server-side EBICS order type in EBICO.Server.
  Use for a new/changed order handler (key management such as INI/HIA/HPB/HCA/HCS/SPR/HSA)
  OR a new upload/download processor for business orders (e.g. CCT/CDD/CDB/CIP, STA/VMK/C53/C52/C54,
  HTD/HKD/HAA/HPD/HAC/PTK, HVU/HVZ/HVD/HVT/HVE/HVS). Covers DI registration, multi-version dispatch,
  BTF resolution, authorisation and the Definition of Done (tests + docs + ADR + coverage matrix).
---

# Creating an EBICS order handler / processor

Two separate extension points — decide first which one fits:

- **Order handler** (`IEbicsOrderHandler`): the *handle* stage of the pipeline for **key/administrative
  orders** that lead straight to a response (INI, HIA, HPB, HCA, HCS, SPR, HSA).
- **Upload/download processor** (`IUploadOrderProcessor` / `IDownloadOrderProcessor`): the
  order-type-specific processing **inside the transaction engine** for **business orders**
  (payments, statements, status/protocol and VEU orders).

Always read `docs/server/order-coverage-matrix.md` first (source of truth: which order types already
exist per version and where the gaps are).

## Variant A — order handler (key management)

Interface: `src/EBICO.Server/Pipeline/IEbicsOrderHandler.cs`
- `EbicsVersion Version` · `string OrderType` · `Task<EbicsOrderResult> HandleAsync(EbicsRequestContext, CancellationToken)`.
- `EbicsOrderResult(EbicsReturnCode ReturnCode, EbicsKeyManagementPayload? Payload = null)` — `Payload`
  only for a successful download key order (HPB), otherwise `null`.

Pattern (see INI/HIA/HPB as templates):
1. Base class `<Xxx>OrderHandlerBase` in `src/EBICO.Server/Handlers/` — version-agnostic flow,
   state transitions, store access, return code logic.
2. One subclass per version, `H003<Xxx>OrderHandler`, `H004<Xxx>OrderHandler`, `H005<Xxx>OrderHandler` —
   only the version-specific part (H003/H004: `RSAKeyValue`; H005: X.509). HSA exists for H003/H004 only.
3. Register in `src/EBICO.Server/DependencyInjection/EbicoServerServiceCollectionExtensions.cs` with
   **`services.AddSingleton<IEbicsOrderHandler, H00xXxxOrderHandler>()`** — one line per version.
   NOT `TryAdd`: `EbicsOrderHandlerResolver` consumes the whole `IEnumerable<IEbicsOrderHandler>`
   and matches on `(Version, OrderType)`.

## Variant B — upload/download processor (business orders)

Interfaces: `src/EBICO.Server/Orders/IUploadOrderProcessor.cs`, `IDownloadOrderProcessor.cs`
- `bool CanProcess(string? effectiveOrderType)` + `ProcessAsync(...)`. The engine calls the **first**
  processor whose `CanProcess` matches.
- Templates: `SepaPaymentUploadProcessor`, `VeuSignatureUploadProcessor` (upload);
  `StatementDownloadProcessor`, `SubscriberInfoDownloadProcessor`, `CustomerProtocolDownloadProcessor`,
  `VeuOverviewDownloadProcessor` (download).

Register with **`AddSingleton`** as well (not `TryAdd`) in `AddEbicoServer`, so the defaults coexist
and a caller can put their own processors in front of them.

## BTF/order type resolution & authorisation

- The generic carriers (H005 `BTU`/`BTD`+BTF · H003/H004 direct code · H003/H004 `FUL`/`FDL`+
  `FileFormat`) are mapped onto the effective classic code (`EffectiveOrderType`, e.g. `CCT`) via
  `BtfOrderTypeCatalog.ResolveUploadOrderType` / `ResolveDownloadOrderType`
  (`src/EBICO.Core/Btf/BtfOrderTypeCatalog.cs`). New order ⇒ add a catalogue entry.
- Strict authorisation: `Subscriber.HasPermissionFor` → on failure return code `090003`
  (`EBICS_AUTHORISATION_ORDER_TYPE_FAILED`). Return codes live centrally in `EBICO.Core.ReturnCodes`.

## Definition of Done (see skill `ebics-feature-workflow`)

- **Tests:** per version + happy/negative. Handler → `tests/EBICO.Tests/Server/<Xxx>OrderHandlerTests.cs`;
  business orders → the matching folder under `tests/EBICO.Tests/{Core,Server}`.
- **Docs:** new page `docs/server/<name>.md` **and** an entry in `docs/index.md`.
- **Coverage matrix:** extend `docs/server/order-coverage-matrix.md` — otherwise the guard test
  `OrderCoverageMatrixTests` fails.
- **ADR:** new decision as `docs/adr/NNNN-<kebab-title>.md` (next free number) + in the ADR index.
- **Spec caveats** made explicit in the docs/test text (ES/A00x unverified, unsigned response, etc.).

## Sources

- Code: `src/EBICO.Server/{Handlers,Orders,Pipeline,Transactions,DependencyInjection}`, `src/EBICO.Core/Btf`.
- Docs: `docs/server/ini.md`, `docs/server/hia.md`, `docs/server/hpb.md`, `docs/server/hca-hcs-spr-hsa.md`,
  `docs/server/payment-orders.md`, `docs/server/statement-orders.md`, `docs/server/status-protocol-orders.md`,
  `docs/server/veu-orders.md`, `docs/server/btf-framework.md`, `docs/server/order-coverage-matrix.md`.
- ADRs: 0012 (return codes), 0016 (BTF/authorisation), 0017 (payments), 0018 (statements),
  0019 (status/protocol), 0020 (VEU).
