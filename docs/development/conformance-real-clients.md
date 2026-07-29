# Conformance against real clients

> Implementation of **issue #59** (Milestone M8 — Validation & Conformance) and completion of the M8 epic
> ([#56](../ticket-overview.md)). This page describes how EBICO is tested against **real, third-party EBICS clients**
> — not only against its own counterpart as in [#57](e2e-connector-server.md)/[#58](negative-security-cases.md).
>
> Deliberately **included**: a new test tier `tests/EBICO.Tests/Conformance/` with committed captures of a
> real third-party client (`ebics-client`/node-ebics-client, MIT), parser/wire-shape tolerance tests, a
> C14N algorithm test, two known-gap negative cases and a skip-if-missing XSD validation. Result:
> a **compatibility matrix** and a **deviations** section.
>
> Deliberately **not yet**: a complete third-party client palette.
>
> **Addendum [#117](#fixed-deviations-117):** the deviations found by #59 are **fixed**
> ([ADR-0029](../adr/0029-interop-fixes-reale-clients.md)). The vendor replay is thereby no longer a
> characterization test of a defect, but the **positive conformance evidence**: the
> committed third-party client bytes drive the full onboarding chain INI → HIA → HPB through.

## Purpose

Until M8 every EBICO side was tested only against a **model of the respective other**: the connector against
fake bank responses, the server against hand-built request XML, and since #57 both against each other. A
wire-format assumption that **EBICO connector and EBICO server share consistently, but a real third-party client
does not**, stays invisible in all these setups. This is exactly the class #59 closes, by running real
third-party client bytes through the real server pipeline.

And the setup delivered immediately: EBICO accepted **none** of the real client's onboarding requests —
a finding that EBICO↔EBICO tests could not make by construction. Behind it lay further defects, each
only becoming visible after the fix before it. All are fixed with #117; the analysis is in
[Fixed deviations](#fixed-deviations-117).

## Test levels

All tests live under `tests/EBICO.Tests/Conformance/` (xUnit v3 + AwesomeAssertions) and run against
the in-process hosted server (`WebApplicationFactory<Program>`), reused via
[`EbicsE2EHarness`](e2e-connector-server.md).

| Level | File | What it checks | CI |
| --- | --- | --- | --- |
| **Vendor capture** | `VendorCaptureConformanceTests` | Sequential replay of real node-ebics-client requests (INI → HIA → HPB) up to `SubscriberState.Ready` | ✅ (captures committed) |
| **Wire-shape tolerance** | `OnboardingWireShapeConformanceTests` | Onboarding with reindented / commented / prefixed XML | ✅ |
| **Signed C14N** | `SignedRequestCanonicalizationConformanceTests` | X002 verification with inclusive **and** exclusive C14N | ✅ |
| **Negative / known-gap** | `WireShapeNegativeConformanceTests` | H005 `RSAKeyValue` instead of certificate; uncompressed order data | ✅ |
| **XSD validation** | `SchemaValidationConformanceTests` | EBICO output against official XSDs | ⏭️ skip-if-missing (Tier B) |

### Honesty boundary (what the Tier-A levels do *not* prove)

- **Wire-shape tolerance** starts from **EBICO's own** request XML and reshapes it. This checks real
  parser robustness (namespace prefix instead of default, whitespace, comments), but is **not** evidence of
  conformance against a foreign *emitter* — the payload still comes from EBICO.
- **Signed C14N** proves that the server reads the canonicalization **algorithm URI from the message**
  (`C14nAlgorithms.FromAlgorithmUri`), not that EBICO's C14N octets match those of a
  third-party library byte for byte — only a captured third-party signature can show that.
- Only the **vendor captures** truly deliver on "tested against a real client".
- The vendor replay too checks **no third-party signature**: the HPB capture does carry a real
  X002 `AuthSignature` from node-ebics-client, but `X002EbicsRequestVerifier` skips
  `ebicsNoPubKeyDigestsRequest` (the request bootstraps the key exchange, ADR-0023). EBICO's
  C14N octets are therefore still **not** verified against a foreign signer. Only a
  capture of a *signed* `ebicsRequest` (after completed onboarding) would deliver on that.

## Compatibility matrix

Real clients × EBICS version × onboarding order, as of this commit. Legend: ✅ accepted ·
❌ rejected · `–` not captured.

| Client | Version | INI | HIA | HPB | Status |
| --- | --- | :---: | :---: | :---: | --- |
| [`ebics-client`](https://github.com/node-ebics/node-ebics-client) (node-ebics-client) 5.0.0 | H004 | ✅ | ✅ | ✅ | **Compatible** since [#117](#fixed-deviations-117) — the chain drives the subscriber up to `Ready` |

Further clients/versions are not yet captured; the corpus loader (`VendorCaptureCorpus`) and the
directory structure `Conformance/Vendor/<client>/<version>/request/` are designed to add them
skip-if-missing (see [Capture guide](#capture-guide)).

> The **EBICO connector itself** covers H003/H004/H005 completely ([#57](e2e-connector-server.md)),
> but here does not count as a "real third-party client" — it shares EBICO's wire assumptions.

## Deviations

### Fixed deviations (#117)

The findings lay **one behind the other on the same path**: each masked the next, which is why #59
knew only the first (and its misclassification). Decisions and rejected alternatives:
[ADR-0029](../adr/0029-interop-fixes-reale-clients.md).

#### 1. `OrderDetails` required an `xsi:type` (critical, blocked real clients)

EBICO's generated bindings typed the `OrderDetails` element (in the static header of
`ebicsUnsecuredRequest` **and** `ebicsNoPubKeyDigestsRequest`) as the **abstract** base type
`OrderDetailsType` — `xscgen` does not translate the XSD `<restriction>` that types the element more
concretely. The `XmlSerializer` then needs an `xsi:type` discriminator, which EBICO's **own** connector
emitted (that is why EBICO↔EBICO was green), but a real client omits:

```
System.InvalidOperationException: The specified type is abstract:
  name='OrderDetailsType', namespace='urn:org:ebics:H004', at <OrderDetails>.
```

Consequence: **all three** onboarding requests were rejected.

**Fix:** `OrderDetailsType` is **concrete** in all three versions; the `[XmlInclude]` attributes remain,
so `xsi:type` is still *accepted*, but no longer *required*. The connector emits the
base type and thus no discriminator at all anymore. Because this is an intervention into generated code,
`scripts/generate-bindings.sh` reapplies it via `apply_binding_fixups()` after every run and aborts
if the pattern is missing; `OrderDetailsBindingTests` pins both directions down. See
[XSD bindings → Manual fixups](../protocol/xsd-bindings.md#manual-fixups-after-generation).

#### 2. Misclassification: `061099` instead of `091010`

Non-mappable client XML was answered with `061099 EBICS_INTERNAL_ERROR` — EBICO blamed **itself**
for a foreign document. The `EbicsErrorMapper` only caught
`InvalidOperationException { InnerException: XmlException }`; the XmlSerializer type exception carries a
different inner type and fell through to `InternalError`.

**Fix:** `EbicsXmlSerializer.DeserializeEnvelope` translates mapping errors of the `XmlSerializer` into
`EbicsEnvelopeFormatException` → `091010 EBICS_INVALID_XML`. Deliberately at the envelope boundary, not in the
error mapper: only there is it known that the bytes come from the client. The order-data path stays
untouched (`OrderDataFault` → `090004`).

#### 3. `A006` (RSASSA-PSS) on H004

node-ebics-client signs its INI order data with **`A006`** (`SignatureVersion`); EBICO permitted `A006`
only for **H005**, so the H004 INI failed on it with `090004`.

**Fix:** `KeyVersions` permits `A006` for **H004 and H005**; H003 (EBICS 2.4) stays excluded.
⚠️ **Spec caveat:** the evidence is a real client plus the common reading (EBICS 2.5 Annex 1
knows A005 **and** A006) — against the official annexes (proprietary, not in the repo) this is **not**
verified.

#### 4. `ds:Modulus` with ASN.1 sign byte (only visible after 1.–3.)

`ds:Modulus` is, per XML-DSig, a `CryptoBinary` without a leading zero; real clients still send, when the
highest bit is set, the 257-byte INTEGER form (`AM/PbALU…`). `RsaKeyMaterial` did normalize the
externally visible bytes (fingerprint, `KeySizeBits` = 2048), but imported the **raw** parameters
— which yielded a 2056-bit RSA instance whose OAEP operations failed. HPB could therefore not encrypt the
bank keys for the subscriber and answered with `090004`.

**Fix:** `RsaKeyMaterial` imports from the **canonical** form; the three views of the same
key (exposed bytes, `KeySizeBits`, imported RSA instance) match up again.

### Persisting spec caveats (consolidated)

From [#57](e2e-connector-server.md)/[#58](negative-security-cases.md) and the
[order coverage matrix](../server/order-coverage-matrix.md), bundled here:

- **Server responses are unsigned** (the connector checks no response signature).
- **ES/A00x order signature** is not verified server-side.
- **camt fixed to `.001.08`**; no real ISO 20022 XSD validation.
- **HAC/PTK** as an own projection instead of a spec-accurate camt.086/pain.002; **HVT** order-summary.
- **BTF catalog** is best-effort against the proprietary External Code List.
- **C14N** of X002 is not verified byte for byte against the official annexes — nor against
  a foreign signer (the vendor replay covers only unsigned or verification-free requests).
- **Binding concretization of `OrderDetails`** and **`A006` on H004** are evidenced against a real client,
  but **not** against the official XSDs/annexes (ADR-0029).

## Capture guide

The captures are produced **once, locally, offline** with the tool under [`tools/vendor-capture/`](../../tools/vendor-capture/README.md):

```bash
cd tools/vendor-capture
npm install        # ebics-client (MIT) — nur lokal, nie in der CI
node capture.js
```

The client posts against a local throwaway sink (never a real bank); only the request is captured and
written to `tests/EBICO.Tests/Conformance/Vendor/node-ebics-client/H004/request/{ini,hia,hpb}.xml`.
Details/license/throwaway keys: the
[`PROVENANCE.md`](../../tests/EBICO.Tests/Conformance/Vendor/node-ebics-client/PROVENANCE.md) in the corpus.

**Adding another client:** place its requests under
`Conformance/Vendor/<client>/<version>/request/*.xml` (the path is **not** `.gitignore`d, see
[ADR-0026](../adr/0026-konformitaet-gegen-reale-clients.md)), enclose a `PROVENANCE.md` and add a
replay in `VendorCaptureConformanceTests` with the expected return codes. The
`HostID`/`PartnerID`/`UserID` used in the capture must be seeded as master data — the replay
onboards a real subscriber, it does not stub it. If the corpus is missing, the replays skip — CI
stays green.

> **Do not commit:** official ebics.org sample XML and XSDs stay proprietary and `.gitignore`d
> (`tests/**/Fixtures/Xml/**/*.xml`, `schemas/**/*.xsd`). The vendor corpus is expressly something
> different: output of a permissively (MIT/Apache) licensed OSS client.

## Tests

- `tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs` — sequential replay of the committed
  node-ebics-client captures (INI → HIA → HPB up to `SubscriberState.Ready`).
- `tests/EBICO.Tests/Serialization/OrderDetailsBindingTests.cs` — guard for the binding fixup:
  `OrderDetailsType` not abstract, reception with **and** without `xsi:type`, output without (#117).
- `tests/EBICO.Tests/Conformance/OnboardingWireShapeConformanceTests.cs` — parser/wire-shape tolerance
  (`XmlShape`: reindent, comments, namespace prefix) across H003/H004/H005.
- `tests/EBICO.Tests/Conformance/SignedRequestCanonicalizationConformanceTests.cs` — inclusive/exclusive
  C14N against the X002 verifier.
- `tests/EBICO.Tests/Conformance/WireShapeNegativeConformanceTests.cs` — H005 `RSAKeyValue` gap and
  uncompressed order data (each `090004`).
- `tests/EBICO.Tests/Conformance/SchemaValidationConformanceTests.cs` — XSD validation (Tier B,
  skip-if-missing).
- `tests/EBICO.Tests/Docs/ConformanceMatrixTests.cs` — docs guard (keeps this page in sync with the mandatory
  sections).

## Related documentation

- [E2E: Connector ↔ Server (happy paths)](e2e-connector-server.md) — the base harness (#57)
- [E2E: negative & security cases](negative-security-cases.md) — X002 verification, tampering (#58)
- [Test harness & fixtures](testing.md) — Tier A/B, `Conformance/`, `SampleXml`, `CanonicalXmlComparer`
- [Order/BTF coverage matrix](../server/order-coverage-matrix.md) — order × version × status
- [ADR-0026 — Conformance against real clients](../adr/0026-konformitaet-gegen-reale-clients.md)
- [ADR-0029 — Interop fixes for real clients](../adr/0029-interop-fixes-reale-clients.md) — the
  remediation of the deviations found here (#117)
- [XSD bindings](../protocol/xsd-bindings.md) — the fixup step at the generator
- [License & repo policy](../legal/ebics-licensing.md) — proprietary schemas/examples vs. OSS output
