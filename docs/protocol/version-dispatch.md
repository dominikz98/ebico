# Version abstraction / protocol dispatch (H003/H004/H005)

The central abstraction in `EBICO.Core`, over which server and connector work
version-dependently without duplicating the logic three times. It is the
pivot between the `EbicsVersion` enum and the generated
[XSD bindings](xsd-bindings.md). Issue **#14** (Milestone M1),
design: [ADR-0004](../adr/0004-multi-version-strategie.md).

## Building blocks

All under `src/EBICO.Core/` (bindings under `Schema/`, the rest under `Versioning/`):

| Building block | Location | Purpose |
|---|---|---|
| `EbicsVersion` (enum) | `EbicsVersion.cs` | `H003`/`H004`/`H005` — the discriminator |
| `EbicsVersionInfo` | `Versioning/EbicsVersionInfo.cs` | immutable metadata per version (code, namespace, 6 envelope CLR types) |
| `EbicsVersions` | `Versioning/EbicsVersions.cs` | static registry (single source of truth) + reverse lookups |
| `IEbicsEnvelope` (+ request/response markers) | `Versioning/IEbicsEnvelope.cs` among others | version-independent view of any envelope |
| `EbicsVersionDetector` | `Versioning/EbicsVersionDetector.cs` | detects the version from raw XML (inbound dispatch) |
| `EbicsVersion*Exception` | `Versioning/EbicsVersionExceptions.cs` | errors during detecting/dispatching |

## Registry (`EbicsVersions`)

The one place that wires the enum with schema code, root namespace and the
envelope bindings:

- `All` — all versions, ordered from old (H003) to new (H005).
- `Get(EbicsVersion)` → `EbicsVersionInfo` (throws `ArgumentOutOfRangeException` for an
  undefined enum value). This is how calling code selects the target version:
  e.g. `EbicsVersions.Get(options.Version).RequestType`.
- `TryFromNamespace(string?, out EbicsVersionInfo?)` — reverse lookup via the
  root namespace; knows the **H003 legacy special case**.
- `TryFromCode(string?, out EbicsVersionInfo?)` — reverse lookup via the
  four-character code (e.g. `"H005"`). Both lookups compare **ordinally**
  (case-sensitive) and simply return `false` for unknown/`null`.

| Version | Code | Root namespace |
|---|---|---|
| H003 | `H003` | `http://www.ebics.org/H003` (legacy) |
| H004 | `H004` | `urn:org:ebics:H004` |
| H005 | `H005` | `urn:org:ebics:H005` |

## Envelope interfaces & partial wiring

`IEbicsEnvelope` offers the version-independent view (`Version`, `Revision`,
`ProtocolVersion`). The markers `IEbicsRequestEnvelope` / `IEbicsResponseEnvelope`
separate the send direction (`ebicsRequest`, `ebicsUnsecuredRequest`, `ebicsUnsignedRequest`,
`ebicsNoPubKeyDigestsRequest`) from the receive direction (`ebicsResponse`,
`ebicsKeyManagementResponse`).

`Version`/`Revision` already provide the generated bindings (via their
per-version `IVersionAttrGroup`). Only `ProtocolVersion` is added — derived from the CLR
namespace and therefore reliable, **independent of the (freely choosable)
`@Version` attribute on the wire**.

The wiring happens via **hand-written partial classes**
(`src/EBICO.Core/Versioning/Bindings/EnvelopeBindings.{H003,H004,H005}.cs`):

```csharp
namespace EBICO.Core.Schema.H005;

public partial class EbicsRequest : IEbicsRequestEnvelope
{
    /// <inheritdoc/>
    public EbicsVersion ProtocolVersion => EbicsVersion.H005;
}
```

> **Why not next to the generated files in `Schema/{Hxxx}/`?**
> `scripts/generate-bindings.sh` deletes and recreates these folders on each run
> (`rm -rf`, cf. [XSD bindings → regeneration](xsd-bindings.md#tooling--regeneration)).
> Hand-written code there would be lost. The C# namespace nonetheless stays
> `EBICO.Core.Schema.Hxxx` — folder ≠ namespace, the SDK compiles all `*.cs`.

## Version detection (`EbicsVersionDetector`)

Detects the version of a raw envelope **without** deserializing the whole document
— only the root element is read via `XmlReader`. The
root namespace is the discriminator (resolved via `TryFromNamespace`).

- `Detect(string)` / `Detect(string, bool strict)` / `Detect(Stream, bool strict = false)`
  → `EbicsVersionInfo`. The stream is **not** closed.
- `TryDetect(…, out EbicsVersionInfo?)` → `bool` (non-throwing, lenient variant).

**Lenient as the default:** The `@Version` attribute is free text on the wire;
what is authoritative is the namespace, because it determines which schema applies. `strict: true`
additionally requires that a present `@Version` matches the namespace.

| Input | Result |
|---|---|
| root in a known namespace (incl. H003 legacy) | `EbicsVersionInfo` |
| unknown / missing namespace | `EbicsVersionNotSupportedException` |
| `null` | `ArgumentNullException` (not a version error) |
| empty / whitespace only | `EbicsEnvelopeFormatException` |
| not XML / truncated tag / no root element / DOCTYPE | `EbicsEnvelopeFormatException` |
| `strict` and `@Version` code ≠ namespace code | `EbicsVersionMismatchException` |
| `strict` and `@Version` missing | OK (namespace version) |
| lenient and `@Version` contradicts the namespace | OK (namespace wins) |

> **Security:** The reader runs with `DtdProcessing.Prohibit` and
> `XmlResolver = null` — a `<!DOCTYPE …>` is rejected (XXE hardening), since the
> server processes untrusted XML.

`TryDetect` only swallows `EbicsVersionException` (i.e. empty/faulty/unknown
XML) and then returns `false`; a `null` argument remains an `ArgumentNullException`,
because it is a caller bug, not bad input data.

## Usage

```csharp
// Select the target version (e.g. connector DI: o.Version = EbicsVersion.H005)
var requestType = EbicsVersions.Get(options.Version).RequestType;

// Inbound dispatch in the server: detect the version from the request bytes
var info = EbicsVersionDetector.Detect(rawRequestXml);
// info.Version / info.RequestType → the matching bindings family
```

## Tests

`tests/EBICO.Tests/Versioning/` (Tier A, CI-safe, without proprietary samples):

- `EbicsVersionsTests` — `All` order, `Get` (incl. CLR-type wiring and
  out-of-range), `TryFromNamespace`/`TryFromCode` (known incl. H003 legacy,
  unknown, `null`, case sensitivity).
- `EnvelopeBindingWiringTests` — all 18 envelopes implement the correct
  marker and report the correct `ProtocolVersion`; `Version`/`Revision` round-trip via the
  interface.
- `EbicsVersionDetectorTests` — success and all four exception paths, lenient vs.
  strict, stream, prolog/comment, DOCTYPE hardening, `TryDetect`.

## Related

- [ADR-0004 — Multi-version strategy](../adr/0004-multi-version-strategie.md)
- [XSD bindings](xsd-bindings.md) — the generated classes on which this builds
- [Connector architecture](../connector/architecture.md) — the app-side
  `IEbicsRequest<TResult>` abstraction (a different layer than `IEbicsRequestEnvelope`)
