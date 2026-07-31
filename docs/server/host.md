# Server: Hostable skeleton (ASP.NET Core)

> Implementation of **Issue #25** (Milestone M3 — Server). This page describes the
> *skeleton* of the EBICS emulator host (`EBICO.Server`): the HTTP endpoint, the
> request pipeline with **Verify/Handle as extension points** (No-Op defaults),
> the central error mapping onto EBICS return codes, and the pluggable
> in-memory state store. The actual order processing (INI/HIA/HPB
> and the transaction engine) follows in **#26 ff. / M4**. The return code catalogue is
> deliberately provisional (full catalogue → **#36 / M4**), the union with the
> Suite read model (`IEmulatorStateProvider`) is **M4**.

## Purpose

`EBICO.Server` is the EBICS server emulator — conceptually like *Azurite* for Azure
Storage, but for EBICS. #25 delivers the *skeleton*: a hostable ASP.NET Core host
that accepts EBICS requests over HTTP, sends them through a testable pipeline and
returns well-formed EBICS responses. Business order logic comes in the
follow-up issues; here stand the load-bearing structure and its extension points.

Deliberately **included in the skeleton**: HTTP endpoint, parsing, version dispatch,
error mapping, response serialisation, DI wiring, state-store abstraction.
Deliberately **not yet**: order handlers (INI/HIA/HPB), signature/encryption checking,
response signature (X002), persistence, segmentation.

## Host & `Program.cs`

The host is wired via a single DI extension and maps the EBICS endpoint
onto a configurable path (default `/ebics`):

```csharp
// Program.cs
builder.Services.AddEbicoServer();

var app = builder.Build();
var options = app.Services.GetRequiredService<IOptions<EbicoServerOptions>>().Value;
app.MapEbicsEndpoint(options.EndpointPath);   // POST, text/xml
app.Run();

public partial class Program;   // so integration tests can use WebApplicationFactory<Program>
```

`AddEbicoServer(Action<EbicoServerOptions>?)` registers the pipeline, the
extension points with their skeleton defaults, the error mapper, the response factory
and the in-memory state store. All concrete services are registered with `TryAdd*`,
so callers can override every building block beforehand (pattern like `AddEbicoConnector`).

`EbicoServerOptions` controls the endpoint path, fallback response version (when the
request version is not recognisable), maximum body size and the accepted
content types. Since **M4** additionally the transaction engine parameters: segment size
(`SegmentSizeBytes`), the segment upper bounds (`MaxUploadSegments`/`MaxDownloadSegments`)
and — with [#35](transaction-recovery.md) — the transaction idle timeout
(`TransactionTimeout`), the sweep interval of the cleanup service
(`TransactionCleanupInterval`) and the upper bound on concurrent transactions
(`MaxConcurrentTransactions`).

## Request pipeline

The endpoint handler stays thin: it reads the body transport-safely and delegates to
`IEbicsRequestPipeline.ProcessAsync(string) → EbicsPipelineResult`. The pipeline is
**HTTP-free** (string in, bytes out) and therefore unit-testable without a web host.

| Stage | Implementation | Error path → return code |
| --- | --- | --- |
| **Parse** | `EbicsXmlSerializer.DeserializeEnvelope(xml)` (Core) | malformed/empty **or well-formed-but-not-mappable** (#117) → `091010` |
| **Version dispatch** | root namespace → version, root element → envelope type; cast to `IEbicsRequestEnvelope` | unsupported version / no request envelope → `061002` |
| **Verify** | `IEbicsRequestVerifier.VerifyAsync` (default: No-Op → success) | failure → `061001` |
| **Handle** | `IEbicsOrderHandlerResolver.Resolve(version, orderType)` (skeleton: no handler) | no handler → `091006`; empty/unknown order type → `091005` |
| **Respond** | `EbicsResponseFactory.BuildErrorResponse(version, code)` → `SerializeToUtf8Bytes` | — |

Parsing and version dispatch are reused from `EBICO.Core`
([Version dispatch](../protocol/version-dispatch.md)); the parsing is hardened against
DTD/XXE (`DtdProcessing.Prohibit`, `XmlResolver = null`), since the server accepts
untrusted XML.

### Extension points

The stages **Verify** and **Handle** are interfaces with skeleton defaults, onto which the
M3/M4 features dock:

| Type | Role | Skeleton default |
| --- | --- | --- |
| `IEbicsRequestVerifier` | signature/state checking (X002, HostID/User, subscriber state) | since #58 `X002EbicsRequestVerifier` (checks the X002 signature of signed `ebicsRequest`, [details](../development/negative-security-cases.md)); the original skeleton was `NoOpEbicsRequestVerifier` |
| `IEbicsOrderHandler` | processing exactly one order type of one version | *no registration* |
| `IEbicsOrderHandlerResolver` | resolution `(Version, OrderType) → Handler` | `EbicsOrderHandlerResolver` over `IEnumerable<IEbicsOrderHandler>` (empty) |

Since no handler is registered, the skeleton answers every recognised request with
`EBICS_UNSUPPORTED_ORDER_TYPE` (`091006`) — enough to demonstrate the pipeline end-to-end.

### Body reading (transport-safe)

`EbicsRequestReader` checks the content type (default `text/xml`/`application/xml`),
enforces the maximum body size and decodes with the declared charset (default
UTF-8). It parses **no** XML — the (hardened) XML processing lives exclusively in
`EBICO.Core`.

## Error mapping & HTTP semantics

The central exception→return code mapping lives in `EbicsErrorMapper`
(`IEbicsErrorMapper`, pluggable). Pipeline-internal cases (no handler, verify failure)
are set directly in the orchestrator.

| Situation | Return code | HTTP status |
| --- | --- | --- |
| Well-formed request, no handler | `091006` EBICS_UNSUPPORTED_ORDER_TYPE | **200** |
| Empty/unknown order type | `091005` EBICS_INVALID_ORDER_TYPE | **200** |
| Invalid/empty XML | `091010` EBICS_INVALID_XML | **200** |
| Unsupported version / no request envelope | `061002` EBICS_INVALID_REQUEST | **200** |
| Verify failed | `061001` EBICS_AUTHENTICATION_FAILED | **200** |
| Unexpected internal error | `061099` EBICS_INTERNAL_ERROR | **200** |
| Wrong content type | — | **415** |
| Body too large | — | **413** |

**Basic rule:** EBICS is an application protocol *over* HTTP. Protocol and
business errors are answered with **HTTP 200** and the return code in the `ebicsResponse`
— the client evaluates the return code, not the HTTP status. Only genuine
transport errors (content type, size), where the server cannot sensibly answer into the envelope,
lead to HTTP 4xx.

## Return code catalogue (central in `EBICO.Core`)

`EbicsReturnCode` bundles code, symbolic name and the placement (`Kind`): a
**technical** code lands in `header/mutable/ReturnCode`, a **business** code in
`body/ReturnCode`; the respective other place gets `000000`. `EbicsResponseFactory`
builds from it, per version (H003/H004/H005), the typed response graph from the
committed schema bindings.

The catalogue and the registry (`EbicsReturnCodes`) have, since **Issue #36 (M4)**, lived centrally in
`EBICO.Core.ReturnCodes` and are used by server **and** connector; the
exception→return code mapping (`IEbicsErrorMapper`/`EbicsErrorMapper`) stays server-side.
Details, complete code tables and the error behaviour:
[Return code catalogue](../protocol/return-codes.md) and [ADR-0012](../adr/0012-return-code-catalogue.md).

### ⚠️ Spec caveats (to be verified against the official EBICS annexes)

- **Header- vs. body placement** of the codes and possible dual assignment (especially
  `091010` EBICS_INVALID_XML) are to be checked against Annex 1.
- **"Unsupported version"** has no dedicated code in the `ebicsResponse`
  (spec-conformant is version negotiation via HEV); the skeleton maps pragmatically onto
  `061002` in the fallback version.
- The **response signature (X002)** is deliberately absent in the skeleton (= M4); strict clients
  might reject unsigned responses.
- `TransactionPhaseType` serialises, lacking a `*Specified` flag, always `Initialisation` —
  to be checked against schema/spec for a transaction-free error response.

## State store (pluggable, in-memory)

`IEbicsStateStore` is the authoritative server-side state (banks/partners/subscribers,
read **and** write) based on the `EBICO.Core.Domain` aggregates. The default registration
is the thread-safe `InMemoryEbicsStateStore` (modelled on `InMemoryKeyStore` in the connector),
pluggable via `TryAddSingleton`.

```csharp
public interface IEbicsStateStore
{
    Task<IReadOnlyList<Bank>> GetBanksAsync(CancellationToken ct = default);
    Task<Bank?> GetBankAsync(HostId hostId, CancellationToken ct = default);
    Task RegisterBankAsync(Bank bank, CancellationToken ct = default);
    // … partner and subscriber counterparts (subscriber by (HostId, PartnerId, UserId) triple)
}
```

It is the **read/write counterpart** to the read-only `IEmulatorStateProvider` of the Suite
(see [UI skeleton](../suite/ui-shell.md)). Both work on the same
domain aggregates; the merge (in-process or HTTP API, see
[ADR-0009](../adr/0009-blazor-render-mode.md)) happens in **M4** — the Suite read model
stays on the `SampleEmulatorStateProvider` until then.

On this store, **#30** builds the complete **master data management**: the store
was extended with `Remove*` and bank-scoped queries (partner now by (`HostId`, `PartnerId`)),
above it sits the `IMasterDataManager` (referential integrity, cascading deletion,
permission/lifecycle mutation) together with an unauthenticated HTTP admin API
(`MapEbicoAdminApi`). Details: [Master data management](master-data.md).

### Raw-XML capture (`IMessageCaptureStore`, #54)

After serialising the response, the pipeline writes the **raw XML** (request and response) of a
transaction message into the `IMessageCaptureStore` — keyed by transaction ID, with phase and
return code. Only transaction-related messages are captured (key management without a transaction ID
stays out); the in-memory default bounds memory via a ring buffer
(`EbicoServerOptions.MaxMessageCaptureEntries`) and truncation per document
(`MaxCapturedMessageBytes`). It is read exclusively by the
[Suite transaction inspector](../suite/transaction-inspector.md) ([ADR-0021](../adr/0021-message-capture-store.md)).

## Tests

`tests/EBICO.Tests/Server/` covers (xUnit v3 + AwesomeAssertions; request XML is built from
the committed Core bindings — **no** proprietary fixtures needed):

- `EbicsErrorMapperTests` — exception → return code (InvalidXml/InvalidRequest/InternalError, null guard).
- `EbicsResponseFactoryTests` — per version: round-trip via `DeserializeEnvelope`, code placement
  header vs. body, correct version namespace, `ReportText`.
- `InMemoryEbicsStateStoreTests` — round-trip, unknown lookups → `null`, add-or-replace, null guard.
- `EbicoServerServiceCollectionExtensionsTests` — resolvable services, skeleton defaults
  (No-Op verifier, no handlers), options defaults/override, null guard.
- `EbicsRequestPipelineTests` — orchestrator directly: malformed/empty → `091010`, foreign
  namespace → `061002`, well-formed request without handler → `091006`, response envelope
  as input → `061002`.
- `EbicsEndpointIntegrationTests` — via `WebApplicationFactory<Program>`: happy path
  (HTTP 200 + `091006`), malformed/empty → 200 + `091010`, foreign version → 200 in
  fallback version, wrong content type → 415, body too large → 413.

For the integration tests `Microsoft.AspNetCore.Mvc.Testing` was added and a
`FrameworkReference` on `Microsoft.AspNetCore.App` was set in the test project; the global
`Program` type is disambiguated against the Suite's via `extern alias`.

## Related documentation

- [Version dispatch](../protocol/version-dispatch.md) — the detection used in the parse/dispatch step
- [XML serialisation & C14N](../protocol/serialization-c14n.md) — deterministic response serialisation
- [Domain model](../protocol/domain-model.md) — the aggregates behind the state store
- [Client core & configuration](../connector/client-core.md) — model for DI/options/store and provisional `EbicsResult`
- [UI skeleton & navigation](../suite/ui-shell.md) — the Suite counterpart (`IEmulatorStateProvider`)
- [ADR-0004 (multi-version)](../adr/0004-multi-version-strategy.md), [ADR-0009 (Suite render mode)](../adr/0009-blazor-render-mode.md)
