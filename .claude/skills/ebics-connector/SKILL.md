---
name: ebics-connector
description: >-
  Anleitung zur Arbeit am EBICO.Connector — dem NuGet-Client, mit dem eine Anwendung einen EBICS-Server
  anspricht (Mediator-Muster). Verwenden beim Erweitern der Client-API/Send-Pipeline, neuen Onboarding-/
  Upload-/Download-Requests, clientseitiger Sende-Validierung oder beim Packaging/Versionierung der
  veröffentlichten Pakete (EBICO.Core + EBICO.Connector). Deckt IEbicsClient/EbicsResult, den eigenen
  Dispatch (kein MediatR), die DI-Erweiterungen, ITransport/IKeyStore und CalVer/SourceLink ab.
---

# EBICO.Connector (NuGet-Client)

Mediator-Muster: die Anwendung kennt nur `IEbicsClient.Send(request)` und bekommt ein typisiertes
`EbicsResult<T>`; die gesamte EBICS-Komplexität liegt dahinter. Vor Änderungen
`docs/connector/architecture.md` lesen (Pipeline + „vorhanden vs. geplant"-Tabelle).

## Kernabstraktionen

- `src/EBICO.Connector/IEbicsClient.cs`: `Task<EbicsResult<TResult>> Send<TResult>(IEbicsRequest<TResult>, ct)`.
  Technische Fehler (Netz/HTTP/Signatur/XML) als Exceptions; fachliche Returncodes im `EbicsResult<T>`.
- **Eigener Dispatch, kein MediatR** (ADR-0005): der Client löst pro Request den passenden
  `IEbicsRequestHandler<TRequest, TResult>` zur Laufzeit auf.
- **Send-Pipeline pro `Send`:** Validierung → Serialisierung → Komprimieren/E002/A00x → X002 → Transport →
  Verify/Entschlüsseln → Returncode → ggf. Segmente → Deserialisieren.
- Abstraktionen: `ITransport` (`src/EBICO.Connector/Transport`, HttpClient dahinter) und `IKeyStore`
  (`src/EBICO.Connector/Keys`). **Stolperstein:** der `HttpClientTransport` postet gegen die *absolute*
  `EbicsConnection.Url`, nicht die `BaseAddress`.

## DI-Erweiterungen (`src/EBICO.Connector/DependencyInjection`)

- `AddEbicoConnector` (Kern + Config, `EbicsConnectionOptions`), `AddEbicoOnboarding` (INI/HIA/HPB),
  `AddEbicoUpload`, `AddEbicoDownload`. Feature-Requests in der jeweils passenden Extension registrieren.

## Requests erweitern

- **Onboarding** (`docs/connector/onboarding.md`): Schlüsselgenerierung, INI/HIA senden, HPB abrufen +
  Bankschlüssel-Hash-Abgleich, INI-Brief (Text/PDF).
- **Upload** (`docs/connector/upload.md`): generische `UploadRequest` + SEPA-Convenience (CCT/CDD/CDB/CIP);
  zweiphasig (Initialisation → Transfer).
- **Download** (`docs/connector/download.md`): generische `DownloadRequest` + Convenience (STA/VMK/C5x/…,
  HAC/HTD/HKD/…), optionale Parsing-Hooks (`DownloadResult.ParsedAs<T>()`); dreiphasig (… → Receipt).
- **Versions-Dispatch:** H005 `BTU`/`BTD`+BTF · H003/H004 `OrderType`/`FUL`/`FDL`.

## Clientseitige Sende-Validierung (ADR-0025)

`src/EBICO.Connector/Validation` (`RequestValidator`): Stufe 1 = Struktur/BTF + opt-in Berechtigung
über `AllowedOrderTypes`. Beim Hinzufügen neuer Requests die Validierungsregeln mitziehen.

## Packaging (nur EBICO.Core + EBICO.Connector werden veröffentlicht)

- **CalVer** `{JAHR}.{MONAT}.{BUILD}` (ADR-0024), Symbols/SourceLink (`snupkg` + Repo-Commit),
  Paket-README (`src/EBICO.Connector/README.md`), MIT-Lizenz.
- **XML-Doc-Pflicht** an öffentlichen APIs (nur Core + Connector; `GenerateDocumentationFile`).
- Lauffähiges Beispiel: `samples/EBICO.Connector.Quickstart` (startet Server in-process,
  Onboarding→Upload→Download). CI `pack` ist build-only; Publish/Push ist M9/#62.

## Definition of Done

Tests (`tests/EBICO.Tests/Connector`, `.../E2E` für echten Round-Trip — Skill `ebics-conformance-test`),
Doku unter `docs/connector/` + Verlinkung in `docs/index.md`, ggf. ADR. Ablauf: `ebics-feature-workflow`.

## Quellen

- Code: `src/EBICO.Connector/*`, `src/EBICO.Connector/README.md`, `samples/EBICO.Connector.Quickstart`.
- Doku: `docs/connector/architecture.md`, `docs/connector/client-core.md`, `docs/connector/onboarding.md`,
  `docs/connector/upload.md`, `docs/connector/download.md`, `docs/connector/packaging.md`.
  ADR: 0005 (Dispatch ohne MediatR), 0024 (NuGet/CalVer), 0025 (clientseitige Sende-Validierung).
