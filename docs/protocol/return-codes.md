# EBICS return-code catalogue (H003/H004/H005)

The central catalogue of the six-digit EBICS return codes in `EBICO.Core`: technical and
business codes as constants, a registry for lookup and the server-side
exception→return-code mapping. Until now there was only a deliberately provisional, server-local
set of nine codes (skeleton #25) and a parallel `EbicsResult<T>` in the connector. Issue
**#36** (Milestone M4) brings both together into a central catalogue. Conventions:
[ADR-0012](../adr/0012-return-code-catalogue.md) and [ADR-0007](../adr/0007-domain-value-objects-record-struct.md).

> **Scope:** The catalogue provides the codes as constants and maps exceptions onto them.
> The actual response creation (`EbicsResponseFactory`) and the request pipeline remain
> server-side. The HEV/H000 system return codes (`SystemReturnCodeType`) are **not** part of
> this catalogue. The response signature (X002) and the real ES/auth check remain M4.

## Building blocks

The catalogue resides under `src/EBICO.Core/ReturnCodes/` (namespace `EBICO.Core.ReturnCodes`); the
mapping remains server-side under `src/EBICO.Server/ReturnCodes/` and `.../Handlers/`
(`EBICO.Core` must not reference `EBICO.Server`).

| Building block | Location | Purpose |
|---|---|---|
| `EbicsReturnCode` | `Core/ReturnCodes/EbicsReturnCode.cs` | value object (`readonly record struct`): `Code`, `SymbolicName`, `Kind`; static fields per code + `const OkCode` |
| `EbicsReturnCodeKind` (enum) | `Core/ReturnCodes/EbicsReturnCodeKind.cs` | `Technical` (header) / `Business` (body) |
| `EbicsReturnCodes` | `Core/ReturnCodes/EbicsReturnCodes.cs` | registry: `All`, `Get`, `TryFromCode`, `IsSuccess` (model `KeyVersions`) |
| `IEbicsErrorMapper` / `EbicsErrorMapper` | `Server/ReturnCodes/` | exception → `EbicsReturnCode` (central, pluggable mapping) |
| `EbicsOrderDataException` | `Server/Handlers/EbicsOrderDataException.cs` | "order data unreadable" — maps unambiguously to `090004` |
| `OrderDataFault` | `Server/Handlers/OrderDataFault.cs` | encapsulates the decode step of the handlers and throws `EbicsOrderDataException` |

## Structure of a return code

Each code carries the six-digit `Code`, the symbolic EBICS name (`SymbolicName`, serving
as the report text in the header) and the placement (`Kind`): a **technical** code lands in the
`header/mutable/ReturnCode`, a **business** code in the `body/ReturnCode`. The respective other
place gets `000000` (`OkCode`). The report text follows the header code.

```csharp
// Lookup via the registry:
if (EbicsReturnCodes.TryFromCode("091010", out var rc))
{
    // rc.SymbolicName == "EBICS_INVALID_XML", rc.Kind == EbicsReturnCodeKind.Business
}

bool ok = EbicsReturnCodes.IsSuccess("000000"); // true
```

### Reading a response: code and text belong together (#124)

When **evaluating** a response, the placement rule works in reverse, and a trap lurks there: the
`ReportText` exists only **once**, in the header. Whoever reads the code from the winning slot but the
text unquestioned from the header produces contradictions — namely, for every business error, exactly
the combination "error code + `EBICS_OK`".

`EbicsReturnCodes.CombineOutcome(headerCode, headerText, bodyCode)` resolves both **together** and
yields an `EbicsResponseOutcome` (`Code` + `Text`):

| Case | Result code | Result text |
| --- | --- | --- |
| Header ≠ `000000` (technical) | header code | header `ReportText` (fallback `SymbolicName`) |
| Body ≠ `000000` (business) | body code | `SymbolicName` from the registry — **never** the header text |
| both `000000` | `000000` | header `ReportText` |
| body code unknown | body code | `null` (no invented text) |

```csharp
var outcome = EbicsReturnCodes.CombineOutcome("000000", "EBICS_OK", "090005");
// outcome.Code == "090005", outcome.Text == "EBICS_NO_DOWNLOAD_DATA_AVAILABLE", outcome.IsSuccess == false
```

The connector uses this in both envelope base classes (`DownloadEnvelopeBuilderBase` /
`UploadEnvelopeBuilderBase`), so that `EbicsResult.ReturnCode` and `EbicsResult.ReturnText` never
diverge. Previously every business error reported `EBICS_OK` as the text
([ADR-0030](../adr/0030-transport-defaults-and-client-side-veu.md)).

## Catalogue

Values and symbolic names follow EBICS Annex 1. The nine codes used by the running code
count as verified; all further entries were included for completeness and are marked in the XML doc
with `⚠️ Spec-Vorbehalt` (to be verified against the official annexes).

**Technical** (header, `header/mutable/ReturnCode`):

| Code | Symbolic name | Meaning |
|---|---|---|
| `000000` | `EBICS_OK` | no error (also fills the unused slot) |
| `011000` | `EBICS_DOWNLOAD_POSTPROCESS_DONE` | download post-processing done ⚠️ |
| `011001` | `EBICS_DOWNLOAD_POSTPROCESS_SKIPPED` | post-processing skipped (negative acknowledgement) ⚠️ |
| `011101` | `EBICS_TX_SEGMENT_NUMBER_UNDERRUN` | fewer segments than announced ⚠️ |
| `031001` | `EBICS_ORDER_PARAMS_IGNORED` | order parameters ignored (informational) ⚠️ |
| `061001` | `EBICS_AUTHENTICATION_FAILED` | authentication signature invalid |
| `061002` | `EBICS_INVALID_REQUEST` | request not specification-conformant |
| `061099` | `EBICS_INTERNAL_ERROR` | internal server error |
| `061101` | `EBICS_TX_RECOVERY_SYNC` | transaction must be re-synchronised ⚠️ |

**Business** (body, `body/ReturnCode`):

| Code | Symbolic name | Meaning |
|---|---|---|
| `090003` | `EBICS_AUTHORISATION_ORDER_TYPE_FAILED` | subscriber not authorised for order type ⚠️ |
| `090004` | `EBICS_INVALID_ORDER_DATA_FORMAT` | order data unreadable/malformed |
| `090005` | `EBICS_NO_DOWNLOAD_DATA_AVAILABLE` | no download data available ⚠️ |
| `091002` | `EBICS_INVALID_USER_OR_USER_STATE` | subscriber unknown / in the wrong state |
| `091003` | `EBICS_USER_UNKNOWN` | subscriber unknown ⚠️ |
| `091004` | `EBICS_INVALID_USER_STATE` | subscriber in a disallowed state ⚠️ |
| `091005` | `EBICS_INVALID_ORDER_TYPE` | order type invalid/unknown |
| `091006` | `EBICS_UNSUPPORTED_ORDER_TYPE` | order type not supported |
| `091008` | `EBICS_BANK_PUBKEY_UPDATE_REQUIRED` | bank keys must be updated (HPB) ⚠️ |
| `091009` | `EBICS_SEGMENT_SIZE_EXCEEDED` | segment too large ⚠️ |
| `091010` | `EBICS_INVALID_XML` | XML not well-formed/schema-conformant |
| `091011` | `EBICS_INVALID_HOST_ID` | `HostID` unknown ⚠️ |
| `091101` | `EBICS_TX_UNKNOWN_TXID` | transaction ID unknown ⚠️ |
| `091102` | `EBICS_TX_ABORT` | transaction aborted ⚠️ |
| `091103` | `EBICS_TX_MESSAGE_REPLAY` | message of a step replayed ⚠️ |
| `091104` | `EBICS_TX_SEGMENT_NUMBER_EXCEEDED` | more segments than announced ⚠️ |
| `091112` | `EBICS_INVALID_REQUEST_CONTENT` | request content disallowed for the operation ⚠️ |
| `091113` | `EBICS_MAX_ORDER_DATA_SIZE_EXCEEDED` | order data too large ⚠️ |
| `091114` | `EBICS_MAX_SEGMENTS_EXCEEDED` | too many segments ⚠️ |
| `091115` | `EBICS_MAX_TRANSACTIONS_EXCEEDED` | too many parallel transactions ⚠️ |
| `091116` | `EBICS_PARTNER_ID_MISMATCH` | `PartnerID` does not match the transaction ⚠️ |
| `091117` | `EBICS_INCOMPATIBLE_ORDER_ATTRIBUTE` | order attribute incompatible with the order type ⚠️ |

## Exception → return code (error behaviour)

`EbicsErrorMapper` is the **only** source for the exception→code mapping of the
request processing. Order-data errors are surfaced by the handlers as
`EbicsOrderDataException` (via `OrderDataFault.Wrap`); its own type maps
unambiguously, regardless of the cause.

| Exception (group) | Return code | Placement |
|---|---|---|
| `EbicsOrderDataException`; `KeyMaterialException`, `InvalidKeyVersionException`, `KeyVersionNotPermittedException`; `InvalidDataException`, `FormatException`, `CryptographicException` | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` | body |
| `InvalidEbicsIdentifierException`, `InvalidSubscriberStateTransitionException`, `MasterDataException` (`UnknownBank/Partner/Subscriber`) | `091002` `EBICS_INVALID_USER_OR_USER_STATE` | body |
| `EbicsEnvelopeFormatException`; `XmlException`; `InvalidOperationException { XmlException }` | `091010` `EBICS_INVALID_XML` | body |
| `EbicsVersionNotSupportedException`, `EbicsVersionMismatchException` | `061002` `EBICS_INVALID_REQUEST` | header |
| everything else (e.g. a bare `ArgumentException`/`InvalidOperationException`) | `061099` `EBICS_INTERNAL_ERROR` | header |

> **Deliberate decision:** A bare `ArgumentException`/`InvalidOperationException` maps
> **not** to `090004`, but to `061099` — outside the order-data decode step it is a
> server error, not a client data error. The decode step of the handlers (`OrderDataFault.Wrap`)
> translates exactly the low-level errors expected there into `EbicsOrderDataException`, so that the
> context dependency is preserved (order-data XML → `090004`, envelope XML → `091010`).
>
> This exact same context dependency is handled since **#117** by the envelope boundary itself:
> `EbicsXmlSerializer.DeserializeEnvelope` translates the mapping errors of the `XmlSerializer`
> (well-formed but not schema-conformant client XML) into `EbicsEnvelopeFormatException` → `091010`,
> instead of letting them fall through as a bare `InvalidOperationException` onto `061099`. The mapper
> did **not** have to be softened for this — only the place that knows whose bytes they are makes
> the assignment ([ADR-0029](../adr/0029-interop-fixes-real-clients.md)).

## EBICS version relation

The catalogue is version-spanning (H003/H004/H005) — the codes themselves are identical. Only the
placement in the typed `ebicsResponse`/`ebicsKeyManagementResponse` happens via the per-version
committed schema bindings (`EbicsResponseFactory`). The response kind (plain `ebicsResponse` vs.
`ebicsKeyManagementResponse`) depends on the request kind, not on the code.

## Tests

- `tests/EBICO.Tests/Core/ReturnCodes/EbicsReturnCodeTests.cs` — value object: `OkCode`, fields,
  value equality of the `record struct`.
- `tests/EBICO.Tests/Core/ReturnCodes/EbicsReturnCodesTests.cs` — registry: `All` (6-digit,
  unique, named), `Get`/`TryFromCode`, `IsSuccess`, known-answer values against Annex 1 (not just
  self-consistency).
- `tests/EBICO.Tests/Server/EbicsErrorMapperTests.cs` — exception→code for all groups incl.
  fallback → `061099` and `null` throw.
- Error paths end-to-end over the pipeline (broken order data → `090004`, unknown/wrong
  state → `091002`) are covered in the handler tests (`Server/IniOrderHandlerTests.cs` among others,
  via `ServerTestHelpers.ReadReturnCodes`).

Tests are Tier A (CI-safe, without proprietary samples).

## Related

- [Hostable server skeleton](../server/host.md) — pipeline, response creation, HTTP status mapping
- [ADR-0012 — Return-code catalogue](../adr/0012-return-code-catalogue.md)
- [ADR-0007 — Domain value objects as `readonly record struct`](../adr/0007-domain-value-objects-record-struct.md)
- [Connector architecture](../connector/architecture.md) — `EbicsResult<T>` uses `EbicsReturnCode.OkCode`
