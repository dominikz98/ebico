---
name: ebics-connector
description: >-
  Guide to working on EBICO.Connector — the NuGet client an application uses to talk to an EBICS
  server (mediator pattern). Use when extending the client API/send pipeline, for new onboarding/
  upload/download requests, client-side send validation, or for packaging/versioning of the
  published packages (EBICO.Core + EBICO.Connector). Covers IEbicsClient/EbicsResult, the own
  dispatch (no MediatR), the DI extensions, ITransport/IKeyStore and CalVer/SourceLink.
---

# EBICO.Connector (NuGet client)

Mediator pattern: the application only knows `IEbicsClient.Send(request)` and gets back a typed
`EbicsResult<T>`; the entire EBICS complexity sits behind it. Before making changes, read
`docs/connector/architecture.md` (pipeline + "available vs. planned" table).

## Core abstractions

- `src/EBICO.Connector/IEbicsClient.cs`: `Task<EbicsResult<TResult>> Send<TResult>(IEbicsRequest<TResult>, ct)`.
  Technical failures (network/HTTP/signature/XML) as exceptions; business return codes in the `EbicsResult<T>`.
- **Own dispatch, no MediatR** (ADR-0005): per request, the client resolves the matching
  `IEbicsRequestHandler<TRequest, TResult>` at runtime.
- **Send pipeline per `Send`:** validation → serialisation → compress/E002/A00x → X002 → transport →
  verify/decrypt → return code → segments if needed → deserialise.
- Abstractions: `ITransport` (`src/EBICO.Connector/Transport`, HttpClient behind it) and `IKeyStore`
  (`src/EBICO.Connector/Keys`). **Pitfall:** the `HttpClientTransport` posts to the *absolute*
  `EbicsConnection.Url`, not to the `BaseAddress`.

## DI extensions (`src/EBICO.Connector/DependencyInjection`)

- `AddEbicoConnector` (core + config, `EbicsConnectionOptions`), `AddEbicoOnboarding` (INI/HIA/HPB),
  `AddEbicoUpload`, `AddEbicoDownload`. Register feature requests in whichever extension fits.

## Extending requests

- **Onboarding** (`docs/connector/onboarding.md`): key generation, sending INI/HIA, fetching HPB +
  comparing the bank key hash, INI letter (text/PDF).
- **Upload** (`docs/connector/upload.md`): generic `UploadRequest` + SEPA convenience (CCT/CDD/CDB/CIP);
  two-phase (initialisation → transfer).
- **Download** (`docs/connector/download.md`): generic `DownloadRequest` + convenience (STA/VMK/C5x/…,
  HAC/HTD/HKD/…), optional parsing hooks (`DownloadResult.ParsedAs<T>()`); three-phase (… → receipt).
- **VEU** (`docs/connector/veu.md`, #124): `UploadRequest.DistributedSignature` parks an order;
  `Hvu`/`Hvz`/`Hvd`/`Hvt`/`Hve`/`HvsRequest` + `VeuOrderReference` drive the multi-eyes workflow.
- **Version dispatch:** H005 `BTU`/`BTD`+BTF · H003/H004 `OrderType`/`FUL`/`FDL`.
  **Administrative order types** (HTD/HKD/… and VEU) carry **no** BTF and stay on the H005
  `AdminOrderType` — in **both** directions, upload as well as download (since #124/ADR-0030).
- **Segment size:** the default is the shared `EbicsSegmentation.DefaultSegmentSizeBytes` (512 KiB). When
  raising it, always calculate against the counterparty's body limit (`MaxSegmentSizeForRequestBody`),
  otherwise it answers with HTTP 413 instead of a return code.
- **Evaluating responses:** resolve code and report text together via `EbicsReturnCodes.CombineOutcome(…)` —
  never mix the header text into a body code.

## Client-side send validation (ADR-0025)

`src/EBICO.Connector/Validation` (`RequestValidator`): stage 1 = structure/BTF + opt-in authorisation
via `AllowedOrderTypes`. When adding new requests, bring the validation rules along.

## Packaging (only EBICO.Core + EBICO.Connector are published)

- **CalVer** `{YEAR}.{MONTH}.{BUILD}` (ADR-0024), symbols/SourceLink (`snupkg` + repo commit),
  package README (`src/EBICO.Connector/README.md`), MIT licence.
- **Mandatory XML doc** on public APIs (Core + Connector only; `GenerateDocumentationFile`).
- Runnable example: `samples/EBICO.Connector.Quickstart` (starts the server in-process,
  onboarding→upload→download). The CI `pack` job is build-only (regression protection); the authenticated
  **publish/push to nuget.org** runs tag-triggered in `.github/workflows/release.yml`
  (#62, ADR-0027, runbook `docs/development/release.md`).

## Definition of Done

Tests (`tests/EBICO.Tests/Connector`, `.../E2E` for a real round-trip — skill `ebics-conformance-test`),
docs under `docs/connector/` + a link in `docs/index.md`, ADR if applicable. Process: `ebics-feature-workflow`.

## Sources

- Code: `src/EBICO.Connector/*`, `src/EBICO.Connector/README.md`, `samples/EBICO.Connector.Quickstart`.
- Docs: `docs/connector/architecture.md`, `docs/connector/client-core.md`, `docs/connector/onboarding.md`,
  `docs/connector/upload.md`, `docs/connector/download.md`, `docs/connector/packaging.md`.
  ADR: 0005 (dispatch without MediatR), 0024 (NuGet/CalVer), 0025 (client-side send validation).
