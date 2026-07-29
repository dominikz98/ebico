# Authentication signature X002 (H003/H004/H005)

The EBICS **authentication signature** over the request: an XML Digital Signature
(`ds:Signature`) that sits in the `AuthSignature` element between `header` and `body` and
secures **all elements with `authenticate="true"`** — key version **X002**
(RSASSA-PKCS1-v1.5 over **SHA-256**, including Canonical XML 1.0). It protects the integrity
and authenticity of the transport. Builds on the key layer from
[#18](key-representation.md) and the canonicalizer from [#15](serialization-c14n.md).
Issue **#20** (Milestone M2), crypto library:
[ADR-0008](../adr/0008-krypto-bibliothek.md) (`System.Security.Cryptography`, no BouncyCastle).

> **Scope:** This layer only provides **creation and verification** of the `AuthSignature`
> over a serialized request XML. It is delimited from the *bank-technical* signature
> A005/A006 ([#19](bank-signature.md), authorising over order data) and from the encryption
> E002 ([#21](encryption-e002.md)). Automatically setting the `AuthSignature` in the
> send/dispatch path as well as interop verification against real bank samples belong in
> later milestones (M3–M6) — here X002 stays a policy-free crypto primitive.
>
> **Application:** The connector *sets* the `AuthSignature` when sending; the server *verifies* it
> since **#58** for every signed `ebicsRequest` (`X002EbicsRequestVerifier` →
> [Negative & security cases](../development/negative-security-cases.md)). The primitive here
> stays policy-free regardless.

## Building blocks

Under `src/EBICO.Core/Crypto/` (namespace `EBICO.Core.Crypto`):

| Building block | Location | Purpose |
|---|---|---|
| `AuthenticationSignature` (static) | `AuthenticationSignature.cs` | `Sign`/`Verify` of the X002 `AuthSignature` (stateless BCL wrappers) |

Reused from [#15](serialization-c14n.md): `XmlCanonicalizer` (C14N as UTF-8 octets,
node-set overload), `C14nMode`/`C14nAlgorithms` (mode ↔ `@Algorithm` URI). From
[#18](key-representation.md): `RsaKeyMaterial` (`CreateRsa()`, `HasPrivateKey`, `ToPublicOnly()`),
the `KeyVersions` registry (`TryGet`, `Purpose`, `PaddingIntent`) as well as `KeyMaterialException`.
The `ds:` object models (`SignatureType`, `SignedInfoType`, `ReferenceType`, …) come from the
committed [XSD bindings](xsd-bindings.md) under `src/EBICO.Core/Schema/Shared/XmlDsig/`.

## X002 — procedure

The signature contains **two** hashes:

1. **Reference digest** — SHA-256 over the C14N of the **authenticated node set** (the
   `authenticate="true"` subtrees). Result → `ds:Reference/ds:DigestValue`. The reference
   carries `URI="#xpointer(//*[@authenticate='true'])"`, a C14N `ds:Transform` and
   `ds:DigestMethod`.
2. **SignatureValue** — RSA signature (PKCS1-v1.5 over SHA-256) over the C14N of the
   `ds:SignedInfo`. The padding variant is resolved **registry-driven** from
   `KeyVersionInfo.PaddingIntent` (not hard-coded): X001/X002 → `RSASignaturePadding.Pkcs1`.

| Element | `@Algorithm` URI |
|---|---|
| `ds:CanonicalizationMethod` / `ds:Transform` | `http://www.w3.org/TR/2001/REC-xml-c14n-20010315` (inclusive, default) |
| `ds:SignatureMethod` | `http://www.w3.org/2001/04/xmldsig-more#rsa-sha256` |
| `ds:DigestMethod` | `http://www.w3.org/2001/04/xmlenc#sha256` |

**Document-context canonicalization.** Both C14N steps run in the context of the envelope: the
signed material inherits the namespace declarations of the request root (protocol namespace as
default, `ds` prefix). Inclusive C14N renders these at the apex of the node set — so the canonical
header carries e.g. `xmlns="urn:org:ebics:H005"`, exactly as a counterpart produces and expects.
For the SignedInfo C14N, the (`ds`-prefixed-only) `ds:SignedInfo` is grafted into a clone of the
request DOM and canonicalized as a subtree; the **same** seam serves signing and verification, so
round-trips stay symmetric.

```csharp
// Request serialisieren (AuthSignature noch leer/abwesend), dann signieren:
string requestXml = EbicsXmlSerializer.SerializeToString(request, EbicsVersion.H005);
SignatureType auth = AuthenticationSignature.Sign(requestXml, signerKey, KeyVersion.Create("X002"));
request.AuthSignature = auth;

// Serverseitig verifizieren (über das empfangene Wire-XML + die deserialisierte AuthSignature):
bool ok = AuthenticationSignature.Verify(requestXml, request.AuthSignature, signerPubKey, KeyVersion.Create("X002"));
```

The `AuthSignature` element is itself **not** `authenticate="true"` and therefore does not
affect the digest — `Verify` works regardless of whether the supplied `requestXml` already
contains the signature.

## Spec caveat

> **⚠️ Spec caveat:** The exact **C14N mode** (inclusive vs. exclusive), the
> **reference selector** (`#xpointer(//*[@authenticate='true'])` and its XPath realisation
> `(//. | //@*)[ancestor-or-self::*[@authenticate='true']]`) as well as the
> **SignedInfo canonicalization context** are EBICS spec details that are **not yet verified
> against the official annexes** (the XSDs are proprietary and not in the repo —
> cf. `CLAUDE.md` and [serialization-c14n.md](serialization-c14n.md)). They are confined to
> constants and the `c14n` parameter respectively; the default is `Inclusive`. Internally
> consistent sign-→-verify round-trips and the deterministic known-answer vector remain
> unaffected by the choice. The byte-exact interop against real banks is validated via a Tier B
> test as soon as a sample is available locally.

## Error behaviour

| Condition | Behaviour |
|---|---|
| `requestXml` / `authSignature` / `key` == `null` | `ArgumentNullException` |
| Signing without a private key | `KeyMaterialException` |
| `version` not a known **authentication** version (`A005`, `E002`, `X999`, `default`) | `InvalidOperationException` |
| Verify: wrong key, tampered authenticated element, tampered `SignatureValue`/`DigestValue`, missing/empty `SignedInfo`/`Reference`/`SignatureValue`, unknown/unsupported algorithm URI | returns `false` (does **not** throw) |

> **No version-permission check here:** Whether a version is permitted with an EBICS protocol
> version remains the task of `KeyVersions.EnsurePermitted` in the dispatch/onboarding layer.
> This primitive stays policy-free. The `false`-instead-of-throw path during verification keeps
> the server robust: a faulty client signature is a clean rejection, not a crash.

## EBICS version relation

The procedure (digest over `authenticate="true"` + RSA signature over `SignedInfo`) is identical
across H003/H004/H005. **X002** is the standard authentication version across all three versions;
the legacy **X001** is covered by the same PKCS1-v1.5 mapping, but is not the target of this issue.
The permitted versions reside centrally in [`KeyVersions`](key-representation.md).

## Tests

`tests/EBICO.Tests/Crypto/AuthenticationSignatureTests.cs` (Tier A, CI-safe, without proprietary samples):

- Happy path Sign → Verify; cross-verify with `ToPublicOnly()`.
- Multiple (including nested) `authenticate="true"` elements in a non-EBICS namespace
  (demonstrates the node-set union and namespace independence).
- Negative cases (returns `false`): tampered authenticated element, tampered
  `SignatureValue`/`DigestValue`, wrong key, unknown `CanonicalizationMethod` URI,
  wrong `SignatureMethod` URI, missing `SignedInfo`/`SignatureValue`.
- Exceptions: `null` arguments, signing without a private key, non-auth/unknown/`default` version.
- **Deterministic X002 known-answer vector**: fixed request XML + fixed PKCS#8 key
  (the same as in `BankSignatureTests`) → byte-identical `DigestValue` **and** `SignatureValue`
  (pins C14N, SignedInfo assembly and padding).
- Document-context evidence: the canonical form of the authenticated nodes contains the inherited
  `xmlns="urn:org:ebics:H005"`.
- Real `EbicsRequest` round-trip (serialize → sign → attach → deserialize → verify).
- Tier B interop against a real bank sample (`SampleXml.TryLoad`, skips when absent).

## Related

- [Bank-technical signature A005/A006](bank-signature.md) — the authorising signature over order data (#19)
- [Encryption E002](encryption-e002.md) — hybrid transport encryption (#21)
- [XML serialization & C14N](serialization-c14n.md) — canonicalizer and C14N modes (#15)
- [Key pairs & representation (A/E/X)](key-representation.md) — the underlying key layer (#18)
- [ADR-0008 — Crypto library](../adr/0008-krypto-bibliothek.md)
