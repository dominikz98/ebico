# 0029 — Interop fixes for real clients (`OrderDetails` without `xsi:type`, `A006` on H004, modulus normalisation)

- Status: accepted
- Date: 2026-07-22

## Context

[ADR-0026](0026-conformance-against-real-clients.md) introduced the vendor-capture tier and
deliberately **only documented rather than fixed** (decision 3 there, "document deviations
instead of fixing the protocol"). The replay of the node-ebics-client captures showed: EBICO
accepts **not a single** onboarding request from a real foreign client. Issue **#117**
catches up on the fix.

Three causally independent defects lay one behind another on the same path — each masked the
next, which is why they only became visible one after another:

1. **`OrderDetails` demands `xsi:type`.** `xscgen` does not translate an XSD `<restriction>`
   that types an element more concretely: `OrderDetails` stays on the **abstract**
   `OrderDetailsType` in the static header of `ebicsUnsecuredRequest` /
   `ebicsNoPubKeyDigestsRequest`. The `XmlSerializer` then demands an `xsi:type`
   discriminator. EBICO's own connector emitted it — which is why EBICO↔EBICO was green — but
   a real client follows the concrete schema type and omits it.
2. **Misclassification.** The resulting, non-mappable client XML was answered with
   `061099 EBICS_INTERNAL_ERROR`: EBICO blamed **itself** for a foreign document. The
   `EbicsErrorMapper` only caught `InvalidOperationException { InnerException: XmlException }`;
   the XmlSerializer type exception carries a different inner type.
3. **`A006`/PSS only on H005** — node-ebics-client signs its H004 INI order data by default
   with `A006`.
4. **Modulus with an ASN.1 sign byte** (only visible after 1.–3.). `ds:Modulus` is per
   XML-DSig a `CryptoBinary` without a leading zero; real clients nonetheless send the
   257-byte INTEGER form when the highest bit is set. `RsaKeyMaterial` did normalise the bytes
   visible **to the outside** (fingerprint, `KeySizeBits`), but imported the **raw**
   parameters — which yielded a 2056-bit key whose OAEP operations failed. HPB could therefore
   not encrypt the bank keys (`090004`).

## Decision

**1. `OrderDetailsType` becomes concrete (base type instead of an XSD-faithful flattening).**
In the generated bindings of all three versions the `abstract` is dropped. The `[XmlInclude]`
attributes and the concrete sub-types stay in place: `xsi:type` is still **accepted**, but no
longer **demanded**. The sub-types (`UnsecuredReqOrderDetailsType`,
`NoPubKeyDigestsReqOrderDetailsType`, `UnsignedReqOrderDetailsType`) carry **no own members**
in H003/H004/H005 — no information content is lost.

**2. The intervention lives in the generator script, not just in the committed `.cs`.**
`scripts/generate-bindings.sh` applies `apply_binding_fixups()` after each run (awk,
CRLF-preserving) and **aborts hard** if the expected pattern is missing. Plus a guard test
(`OrderDetailsBindingTests`) that checks `IsAbstract == false` — a lost fixup thereby shows up
immediately and not only at the next foreign client.

**3. The connector emits the base type.** A sub-class instance would still produce `xsi:type`
(and the `xmlns:xsi` declaration). EBICO's onboarding requests thereby look like those of a
real client — tolerance in both directions, not only on receipt.

**4. Resolve the misclassification at the envelope boundary, not in the error mapper.**
`EbicsXmlSerializer.DeserializeEnvelope` translates `XmlSerializer` mapping errors into
`EbicsEnvelopeFormatException` (→ `091010 EBICS_INVALID_XML`). This is the only place that
*knows* the bytes come from the client. Deliberately **not** in `DeserializeCore`: the generic
`Deserialize<T>` overloads also decode order data, where `OrderDataFault` already maps
specifically to `090004` — a translation there would override that mapping.

**5. `A006` applies to H004 **and** H005.** H003 (EBICS 2.4) stays excluded.

**6. `RsaKeyMaterial` imports from the canonical form.** Modulus/exponent are trimmed *before*
they go into the `RSAParameters` held for `CreateRsa()`. The three views of the same key
(exposed bytes, `KeySizeBits`, imported RSA instance) thereby agree again.

**7. The vendor replay turns from a characterisation into a conformance test.**
`VendorCaptureConformanceTests` seeds the master data and drives the three captures as **one
sequential chain** INI → HIA → HPB up to `SubscriberState.Ready` including the encrypted HPB
response.

## Consequences

- **Real clients work.** The compatibility matrix in
  [conformance against real clients](../development/conformance-real-clients.md) stands at
  ✅ ✅ ✅ for node-ebics-client 5.0.0 / H004. Deviations 1 and 2 from #59 are closed.
- **EBICO's own wire format changes** (onboarding requests without `xsi:type`/`xmlns:xsi`).
  This is secured by the E2E suite (#57) across H003/H004/H005 and makes the output more
  strictly conformant to the xsi-free form promised in
  [serialisation & C14N](../protocol/serialization-c14n.md).
- **The binding is laxer than the XSD at this point** — it accepts `OrderDetails` even where
  the XSD prescribes a particular concrete type. Practically free: the `XmlSerializer` does
  not validate against the XSD anyway; real schema validation stays the tier-B test
  `SchemaValidationConformanceTests` (skip-if-missing).
- **The spec caveat remains.** Neither the concretisation of `OrderDetails` nor `A006` on H004
  is verified against the official XSDs/annexes (proprietary, not in the repo —
  [ADR-0003](0003-handling-proprietary-schemas.md)). The evidence is a real client plus
  the common reading (EBICS 2.5 Annex 1 knows A005 **and** A006). Both are centralised in
  exactly one place (`apply_binding_fixups()` and `KeyVersions` respectively) and revisable in
  one step given better facts.
- **The generator is no longer a pure generator.** Whoever regenerates the bindings must know
  the fixup step; see [XSD bindings](../protocol/xsd-bindings.md), section "Manual fixups".

## Alternatives

- **Flatten the header classes XSD-faithfully** (pull `OrderDetails` + `SecurityMedium` out of
  `StaticHeaderBaseType` into the three derived header types, typed concretely there): rejected
  — it maps the XSD `restriction` correctly, but affects 12 generated files instead of 3, and
  the serialisation order (base members before derived) as well as the position of the
  `xs:any` collection would have to be correct by hand. Significantly more risk for the same
  wire effect.
- **`XmlAttributeOverrides` per envelope root type:** rejected — the bindings would stay
  untouched, but overriding an inherited member is not clearly specified in the
  `XmlReflectionImporter`, and each version × root would need its own override set plus its own
  serializer cache.
- **Be tolerant only on the receiving side (keep emitting `xsi:type`):** rejected — solves only
  half. A strict foreign parser on the other side (a real bank) would still get EBICO's
  discriminator.
- **Extend the `EbicsErrorMapper` with `InvalidOperationException`:** rejected — too broad. An
  `InvalidOperationException` from inside the server is a genuine server error and must stay
  `061099`; only at the envelope boundary is the attribution unambiguous.
- **Leave `A006` on H004 open:** rejected — a real client's INI would still be rejected, and
  the vendor replay would never have uncovered the downstream modulus defect.

## Related decisions

- [ADR-0026 — Conformance against real clients](0026-conformance-against-real-clients.md) —
  produced these findings and explicitly deferred the fix as follow-up work.
- [ADR-0006 — Commit generated XSD bindings](0006-commit-generated-xsd-bindings.md) — why
  the bindings are in the repo at all and a fixup step is needed.
- [ADR-0003 — Handling proprietary schemas](0003-handling-proprietary-schemas.md) — why
  verification against the XSDs/annexes is not possible here.
