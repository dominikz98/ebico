# Connector: Client core & configuration

> Implementation of **issue #46** (milestone M6 — Connector). This page
> describes the foundation of the `EBICO.Connector`: the public abstractions,
> the configuration, the DI registration and the own request dispatch. The
> overarching design is in the [Connector architecture](architecture.md); the
> decision *no MediatR* in
> [ADR-0005](../adr/0005-connector-dispatch-ohne-mediatr.md).

## Purpose

The connector core wires up the building blocks on which the later M6 issues
(onboarding INI/HIA/HPB, upload, download, NuGet packaging) build. A calling
app knows only **one** entry method — `IEbicsClient.Send(...)` — and gets a
typed `EbicsResult<T>`. #46 delivers the *skeleton* (abstractions +
configuration + dispatch + default transport + key store); concrete
requests/handlers come in follow-up issues.

## Public abstractions

All in the namespace `EBICO.Connector`:

| Type | Role |
| --- | --- |
| `IEbicsRequest<TResult>` | Marker: a request "knows" its result type. |
| `IEbicsClient` | Mediator; single entry method `Send<TResult>(request, ct)`. |
| `IEbicsRequestHandler<TRequest, TResult>` | One handler per concrete request type. |
| `EbicsContext` | Execution context created per `Send` (connection, keys, transport, version). |
| `EbicsResult<T>` | Result/return code type (**preliminary**, see below). |
| `EbicsConnectorException` | Base exception; derivations `EbicsConfigurationException`, `EbicsTransportException`. |

```csharp
public interface IEbicsClient
{
    Task<EbicsResult<TResult>> Send<TResult>(IEbicsRequest<TResult> request, CancellationToken ct = default);
}
```

## Configuration

`EbicsConnectionOptions` (namespace `EBICO.Connector.Configuration`) holds the
connection parameters as bindable strings:

| Field | Meaning |
| --- | --- |
| `Url` | absolute HTTP(S) URL of the EBICS server endpoint |
| `HostId` | EBICS `HostID` of the bank/server |
| `PartnerId` | EBICS `PartnerID` (customer) |
| `UserId` | EBICS `UserID` (subscriber) |
| `Version` | target protocol version (`EbicsVersion`, default `H005`) |
| `AllowedOrderTypes` | optional client-side allow-list of permitted (classic) OrderType codes (e.g. `CCT`, `C53`); **empty = no client-side check** (the server remains the authority) |

Before use, the options are validated and converted into the immutable
`EbicsConnection`: the IDs are parsed via the validated Core types
(`HostId`/`PartnerId`/`UserId` from `EBICO.Core.Domain`), the version is bound to
its `EbicsVersionInfo` via the `EbicsVersions` registry.

Validation runs through the options mechanism
(`EbicsConnectionOptionsValidator : IValidateOptions<EbicsConnectionOptions>`):
on an invalid configuration the first resolution of the `EbicsConnection` throws
an `OptionsValidationException` with all problems found (missing/invalid URL,
invalid identifiers, unknown version). Direct calls of
`EbicsConnection.FromOptions(...)` throw an `EbicsConfigurationException` on
invalidity.

## DI registration

```csharp
services.AddEbicoConnector(o =>
{
    o.Url       = "https://bank.example/ebicsweb";
    o.HostId    = "MYHOST";
    o.PartnerId = "PARTNER01";
    o.UserId    = "USER0001";
    o.Version   = EbicsVersion.H005;
})
.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
.AddStandardResilienceHandler();   // optional; Resilienz-Paket beim Aufrufer
```

`AddEbicoConnector(...)` registers the options + their validator, the
`EbicsConnection`, the default `InMemoryKeyStore`, the `HttpClientTransport`, the
`IEbicsClient` as well as a **named** `HttpClient`
(`EbicoConnector.HttpClientName`). The return value is the `IHttpClientBuilder`
of this named client — so timeouts and resilience are configured directly on it
and thus stay out of the connector core (details:
[Architecture → DI registration](architecture.md#di-registration)).

## Transport

`ITransport` (namespace `EBICO.Connector.Transport`) is the narrow transport
abstraction: `SendAsync(EbicsHttpRequest, ct)` → `EbicsHttpResponse`. The
default `HttpClientTransport` obtains its `HttpClient` via `IHttpClientFactory`
(named client), sends the serialized XML envelope via `POST`
(`Content-Type: text/xml; charset=utf-8`) to the configured URL and passes the
`CancellationToken` through. Non-success statuses and HTTP/network errors are
thrown as `EbicsTransportException` — technical errors are exceptions, business
return codes end up in the `EbicsResult<T>`.

## Key store

`IKeyStore` (namespace `EBICO.Connector.Keys`) provides key material
(`RsaKeyMaterial` from `EBICO.Core.Crypto`), addressed via `KeyOwner`
(`Subscriber`/`Bank`) and `KeyPurpose` (`Signature`/`Encryption`/
`Authentication`):

- **`InMemoryKeyStore`** — thread-safe, default registration, ideal for tests.
- **`FileKeyStore`** — one file per key under a configured directory; subscriber
  keys as PKCS#8 (private), bank keys as SubjectPublicKeyInfo (public), via the
  existing `RsaKeyImportExport`. **Security note:** private keys lie
  *unencrypted* on disk — only for development/simple setups; in production use
  an encrypted store or HSM (later issues).

In the skeleton the store is implicitly scoped to the one configured subscriber
connection; multi-subscriber scoping follows later.

## Dispatch (without MediatR)

`Send<TResult>(IEbicsRequest<TResult>)` statically knows only the result type,
not the concrete request type. The client therefore resolves the appropriate
`IEbicsRequestHandler<TRequest, TResult>` via an **own** dispatch (no MediatR,
[ADR-0005](../adr/0005-connector-dispatch-ohne-mediatr.md)):

1. At runtime, a type-bound wrapper
   (`RequestHandlerWrapper<TRequest, TResult>`) is created via
   `request.GetType()` and cached in a `ConcurrentDictionary<Type, object>` —
   reflection only on the first occurrence of a request type, thereafter a
   virtual call.
2. Per `Send` the client opens a DI scope, resolves `EbicsConnection`,
   `IKeyStore` and `ITransport`, builds the `EbicsContext` and calls the wrapper.
3. The wrapper fetches the handler from the scope. If it is missing, it throws an
   `EbicsConfigurationException`.

Handlers are registered by later issues as
`services.AddSingleton<IEbicsRequestHandler<TReq, TRes>, THandler>()` (in tests
via a fake handler).

## Version binding

The target version from `o.Version` is bound to its `EbicsVersionInfo` via the
Core `EbicsVersions` registry and provided in `EbicsContext.Version`; the
handlers' envelope namespaces and header structure build on it
([Version dispatch](../protocol/version-dispatch.md)).

## Client-side validation (stage 1)

Before an upload/download loads keys, computes crypto or touches the transport,
**send pipeline stage 1** runs — the static `RequestValidator` (namespace
`EBICO.Connector.Validation`), wired at the start of
`UploadExecutor`/`DownloadExecutor`. Onboarding (INI/HIA/HPB) does **not** run
through the executors and is therefore never validated here. Two
responsibilities with deliberately separated error semantics:

- **Structure/BTF (always active):** The order identity must be resolvable for
  version and direction (H005 `BTU`/`BTD` + BTF, H003/H004 classic OrderType or
  `FUL`/`FDL` + FileFormat); a code known in the BTF catalog must not be used in
  the wrong direction (e.g. `STA` as an upload); the upload payload must not be
  empty and an explicitly set segment size must be positive. A violation is a
  programming/config error → **`EbicsConfigurationException`**.
- **Authorisation (opt-in):** If `AllowedOrderTypes` is set, a request whose
  **effective classic** OrderType code is not in the list is rejected locally —
  without a server round-trip (fail-fast) — as
  `EbicsResult<T>.Failure("090003", …)` (`EBICS_AUTHORISATION_ORDER_TYPE_FAILED`),
  exactly as the bank would report it. The key is the effective classic code
  (H005 `CCT` matches `"CCT"`, **not** the wire code `"BTU"`); administrative
  codes (HTD/…) are subject to the list as well. An **empty** list (default)
  skips the check and leaves authorisation to the server — the bank remains the
  authority in any case; the allow-list is only a pre-check.

Fundamental decision (static helper, error-semantics asymmetry, deliberate
divergence from the strict server enforcement of ADR-0016):
[ADR-0025](../adr/0025-clientseitige-sende-validierung.md).

## `EbicsResult<T>` — preliminary

`EbicsResult<T>` separates business success (with a value), a business return
code (no error) and technical errors (exception). Instances are created via
`EbicsResult<T>.Success(value, [code], [text])` or
`EbicsResult<T>.Failure(code, [text])`.

> **Preliminary:** The final form and the complete EBICS return code catalog are
> defined in **#36 (M4)**; the connector-local form introduced here keeps #46
> self-contained and is reconciled with #36.

## Tests

`tests/EBICO.Tests/Connector/` covers: `EbicsResult` semantics, options
validation (happy path + negative cases), `EbicsConnection` resolution,
in-memory/file key store round-trips, the dispatch (fake handler; no handler →
`EbicsConfigurationException`) and the `HttpClientTransport` against a stubbed
`HttpMessageHandler` (POST/Content-Type/payload, non-success →
`EbicsTransportException`, cancellation).
