# XML serialization & canonicalization (C14N)

How `EBICO.Core` serializes EBICS envelopes **deterministically** and canonicalizes them for
signatures (C14N). Builds on the committed [XSD bindings](xsd-bindings.md)
(#11–#13) and the [version dispatch](version-dispatch.md) (#14). Issue **#15**
(Milestone M1).

## Building blocks

All under `src/EBICO.Core/Serialization/`:

| Building block | Location | Purpose |
|---|---|---|
| `EbicsXmlSerializer` | `Serialization/EbicsXmlSerializer.cs` | deterministic serialization + version-detecting deserialization of envelopes |
| `XmlCanonicalizer` | `Serialization/XmlCanonicalizer.cs` | canonical form (C14N) as UTF-8 octets; inclusive **and** exclusive |
| `C14nMode` / `C14nAlgorithms` | `Serialization/C14nAlgorithm.cs` | the four C14N variants + mapping to the `ds:CanonicalizationMethod/@Algorithm` URI |

## Deterministic serialization

So that the same object structure always produces the same bytes (and stays structurally equal
across H003/H004/H005), `EbicsXmlSerializer` fixes:

- **UTF-8 without BOM**, with an XML declaration, without indentation
  (`encoding="utf-8"` correct in the declaration — the serialization runs over a
  `MemoryStream`, not over a `StringWriter`, which would declare `utf-16`).
- **Stable prefix map** per version via `XmlSerializerNamespaces`: the protocol namespace
  as the **default** (root unprefixed), `ds` for XML-DSig. This at the same time suppresses the
  automatic `xmlns:xsi`/`xmlns:xsd` noise.
- The **element/attribute order** is already fixed by the generated bindings;
  the serializer only adds encoding, namespaces and formatting deterministically.
- `XmlSerializer` instances are **cached** per type (construction is expensive).

```csharp
var request = new EBICO.Core.Schema.H005.EbicsRequest { Version = "H005" };

byte[] bytes  = EbicsXmlSerializer.SerializeToUtf8Bytes(request); // Wire-Bytes
string xml    = EbicsXmlSerializer.SerializeToString(request);
//            → <?xml version="1.0" encoding="utf-8"?>
//              <ebicsRequest xmlns="urn:org:ebics:H005" xmlns:ds="…" Version="H005" />
```

Symmetrically, `DeserializeEnvelope` **detects** the version itself: the root namespace
selects the version via the [`EbicsVersionDetector`](version-dispatch.md) (incl.
H003 legacy special case), the root **element name** one of the six envelopes
(`ebicsRequest` → `RequestType`, `ebicsResponse` → `ResponseType`, …
`ebicsKeyManagementResponse` → `KeyManagementResponseType`):

```csharp
IEbicsEnvelope envelope = EbicsXmlSerializer.DeserializeEnvelope(rawXml);
// envelope.ProtocolVersion → the detected version; the concrete type depends on the root element
```

Incoming XML is **hardened against DTD/XXE** (`DtdProcessing.Prohibit`,
`XmlResolver = null`) — a `<!DOCTYPE …>` is rejected. Unknown root elements in
a known namespace yield an `EbicsEnvelopeFormatException`, an unknown
namespace an `EbicsVersionNotSupportedException`.

## Canonicalization (C14N)

`XmlCanonicalizer` provides the **canonical form as a UTF-8 `byte[]`** — exactly the bytes over which
an EBICS authentication signature forms its digest. Both families are supported,
selected via `C14nMode`:

| `C14nMode` | Algorithm URI |
|---|---|
| `Inclusive` *(default)* | `http://www.w3.org/TR/2001/REC-xml-c14n-20010315` |
| `InclusiveWithComments` | …`#WithComments` |
| `Exclusive` | `http://www.w3.org/2001/10/xml-exc-c14n#` |
| `ExclusiveWithComments` | …`#WithComments` |

```csharp
byte[] c14n = XmlCanonicalizer.Canonicalize(xml);                       // inklusiv (Default)
byte[] exc  = XmlCanonicalizer.Canonicalize(xml, C14nMode.Exclusive);   // exklusiv
```

- **Whitespace-faithful:** loading happens with `PreserveWhitespace = true` — unlike the
  whitespace-tolerant test helper `CanonicalXmlComparer` (which for comparison purposes
  discards irrelevant formatting), because the canonical octets are the **signed material**.
- **Same hardening** as above (DTD/XXE).
- `C14nAlgorithms.FromAlgorithmUri` / `ToAlgorithmUri` map the URI onto the mode and
  back — so the signature code (M2) can derive the method from a `SignedInfo`.
- The `inclusiveNamespacePrefixList` parameter takes effect only in the exclusive modes
  (`InclusiveNamespaces` prefix list).

> **Inclusive vs. exclusive — core difference:** A namespace declaration declared on an
> ancestor and **unused** in the subtree is kept by *inclusive* C14N, whereas
> *exclusive* leaves it out. This is exactly what the differentiator test vector checks.

> ⚠️ **Spec caveat (default = inclusive).** The issue text names "exclusive C14N", but the
> EBICS authentication signature very probably uses **inclusive**
> Canonical XML 1.0. The official XSDs/annexes are not in the repo (cf.
> [Schema sources](schema-sources.md) and [ADR-0003](../adr/0003-umgang-mit-proprietaeren-schemas.md)),
> so the primitive is deliberately designed for **both** algorithms and the default is set to
> `Inclusive`. The exact algorithm is to be **verified** against the official EBICS annex,
> as soon as the schemas are available; M2 (crypto/signatures) then selects the method
> via the `@Algorithm` URI.

## Relationship to `CanonicalXmlComparer`

The test helper [`CanonicalXmlComparer`](../development/testing.md#canonicalxmlcomparer--canonicalized-xml-comparison)
has delegated since #15 to `XmlCanonicalizer` (mode `Inclusive`) — there is **one**
C14N implementation. The helper additionally stays whitespace-tolerant, because it
compares serializer determinism, not producing signed bytes.

## Tests

`tests/EBICO.Tests/Serialization/` (Tier A, CI-safe, without proprietary samples):

- `XmlCanonicalizerTests` — known C14N vectors (based on W3C C14N 1.0 §3 / exc-c14n,
  DTD-free): attribute/namespace sorting, empty element ↔ explicit close,
  character escaping in text, UTF-8 octets, comment modes, **inclusive-vs-exclusive differentiator**,
  determinism, DOCTYPE/`null`/malformed hardening; plus `C14nAlgorithms` mapping.
- `EbicsXmlSerializerTests` — deterministic, BOM-/xsi-/xsd-free output per
  H003/H004/H005, structurally equal across the versions, stable `ds` prefix on
  `AuthSignature`, round-trip via `DeserializeEnvelope` and XXE hardening.

## Related

- [XSD bindings](xsd-bindings.md) — the generated classes that are serialized here
- [Version dispatch](version-dispatch.md) — `EbicsVersionDetector`/registry, on which the
  deserialization builds
- [Test harness](../development/testing.md) — `CanonicalXmlComparer` (delegates here)
- [ADR-0003 — proprietary schemas](../adr/0003-umgang-mit-proprietaeren-schemas.md) ·
  [ADR-0006 — commit bindings](../adr/0006-generierte-xsd-bindings-committen.md)
