# Key pairs & representation (A/E/X) (H003/H004/H005)

The first crypto layer in `EBICO.Core` (`Crypto/`): type-safe key versions
(A00x/E002/X002), an RSA key container and import/export via PKCS#8, X.509/SPKI,
PEM and the EBICS `RSAKeyValue` representation. Issue **#18** (Milestone M2),
crypto library: [ADR-0008](../adr/0008-crypto-library.md)
(`System.Security.Cryptography`, no BouncyCastle).

> **Scope:** Deliberately only **representation, import/export and version mapping**.
> Signing/verifying (A005/A006 #19, X002 #20), encryption (E002 #21),
> hashing/fingerprints (HPB/INI/HIA #22) and X.509 chain checking (#23) do **not** belong
> here. The `RsaPaddingScheme` details are pure metadata (intent); no crypto operation
> is performed in this layer. The mapping onto the generated
> [bindings](xsd-bindings.md) (`PubKeyInfoType` among others) is also deferred to the
> INI/HIA/HPB order-data issues; #18 only provides `ExportRsaKeyValue` (modulus/exponent) for it.

## Building blocks

All under `src/EBICO.Core/Crypto/` (namespace `EBICO.Core.Crypto`):

| Building block | Location | Purpose |
|---|---|---|
| `KeyPurpose` (enum) + `KeyPurposeExtensions` | `KeyPurpose.cs` | key role signature/enc/auth ↔ version letter `A`/`E`/`X` |
| `KeyVersion` (`readonly record struct`) + `RsaPaddingScheme` (enum) | `KeyVersion.cs` | validated 4-character code (`[AEX]\d{3}`); padding scheme as metadata |
| `KeyVersionInfo` | `KeyVersionInfo.cs` | immutable metadata per version (purpose, legacy, padding intent, permitted EBICS versions) |
| `KeyVersions` | `KeyVersions.cs` | registry/single source of truth + version mapping per EBICS version |
| `RsaKeyMaterial` | `RsaKeyMaterial.cs` | immutable RSA container (public, optionally private); canonical modulus/exponent form |
| `RsaKeyImportExport` | `RsaKeyImportExport.cs` | PKCS#8 / SPKI / X.509 / PEM / `RSAKeyValue` import & export |
| `EbicsCryptoException` (+ derived) | `CryptoExceptions.cs` | errors of the crypto layer |

## Key roles & versions

EBICS distinguishes three RSA key roles, identifiable by the leading version letter:

| Role (`KeyPurpose`) | Letter | Versions | Meaning |
|---|---|---|---|
| `Signature` | `A` | A004/A005/A006 | bank-technical (authorising) signature |
| `Encryption` | `E` | E001/E002 | encryption (transaction key/order data) |
| `Authentication` | `X` | X001/X002 | authentication/identification of requests |

> **Note:** The signature version letter `A` has **nothing** to do with `SignatureClass.A`
> (first signature, see [domain model](domain-model.md)) — same letter,
> different concept.

`KeyVersion.Create` checks only the **form** (letter A/E/X + three digits). A
well-formed but unknown code (`"A999"`) is accepted, but does not resolve via
`KeyVersions.TryGet` — knowledge of known versions resides in the registry.

```csharp
var v = KeyVersion.Create("A005");      // v.Purpose == KeyPurpose.Signature
KeyVersion.Create("a005");              // InvalidKeyVersionException (lowercase)
KeyVersion.TryCreate("E002", out var e);// non-throwing variant
default(KeyVersion).Value;              // null — struct caveat (cf. ADR-0007)
```

## Version mapping per EBICS version

`KeyVersions` is the only place that knows which key version is permitted with which
EBICS protocol version (analogous to the `EbicsVersions` registry).

| Code | Role | Legacy | Padding (metadata) | permitted in |
|---|---|---|---|---|
| A004 | Signature | yes | Pkcs1V15 | H003, H004 |
| A005 | Signature | no | Pkcs1V15 | H003, H004, H005 |
| A006 | Signature | no | Pss | H004, H005 |
| E001 | Enc | yes | Pkcs1V15Encryption | H003, H004 |
| E002 | Enc | no | Oaep | H003, H004, H005 |
| X001 | Auth | yes | Pkcs1V15 | H003, H004 |
| X002 | Auth | no | Pkcs1V15 | H003, H004, H005 |

```csharp
KeyVersions.IsPermitted(KeyVersion.Create("A006"), EbicsVersion.H003);     // false
KeyVersions.EnsurePermitted(KeyVersion.Create("A006"), EbicsVersion.H004); // ok (since #117)
KeyVersions.Default(KeyPurpose.Signature, EbicsVersion.H005).Code;         // "A005" (A006 is opt-in)
KeyVersions.PermittedFor(KeyPurpose.Signature, EbicsVersion.H005);         // A005, A006
```

> **⚠️ Spec caveat:** This table (legacy versions withdrawn in 3.0, A006 from
> EBICS 2.5/H004 on, default A005) follows the common reading and is **not yet verified against the
> official EBICS XSDs/annexes** (cf. CLAUDE.md). It is caught up at this one place
> (`KeyVersions`) once the schemas are available.
>
> For **A006 on H004** there is at least hard evidence from practice: the real OSS client
> node-ebics-client signs its H004 INI order data with A006 by default (vendor capture,
> see [Conformance against real clients](../development/conformance-real-clients.md) and
> [ADR-0029](../adr/0029-interop-fixes-real-clients.md)). H003 remains deliberately excluded.

## Key material: `RsaKeyMaterial`

Immutable container; stores cloned `RSAParameters` instead of a live
`RSA` instance (no `IDisposable`, no use-after-dispose). For an operation,
`CreateRsa()` yields a fresh `RSA` (the caller disposes it). `Modulus`/`Exponent` are output in
**EBICS-canonical form** (unsigned big-endian, without a leading null), so that
later fingerprints (#22) and the order-data layer see the same bytes.

- `FromPublicKey(RSA)` / `FromKeyPair(RSA)` / `FromModulusExponent(mod, exp)`
- `HasPrivateKey`, `KeySizeBits`, `ToPublicOnly()`
- **Minimum key size:** `MinKeySizeBits = 2048` (EBICS allows 1536–4096; revisable
  policy). Smaller keys are rejected on import with `KeyMaterialException`.

> **Canonicalization also applies to import (#117).** The normalisation takes effect not only for the
> outward-visible bytes, but also for the `RSAParameters` that `CreateRsa()` imports.
> Reason: `ds:Modulus` is per XML-DSig a `CryptoBinary` **without** a leading null, but real clients
> send the 257-byte ASN.1 INTEGER form when the most significant bit is set. If that was imported raw,
> a **2056-bit** RSA instance arose whose OAEP/PKCS#1 operations failed — while
> `KeySizeBits` and the fingerprint reported 2048. On receipt EBICO is thus deliberately tolerant
> (Postel) and internally consistent; the canonical form is still emitted.

## Import / export — `RsaKeyImportExport`

Thin wrappers around the BCL ([ADR-0008](../adr/0008-crypto-library.md)); BCL `CryptographicException`
is uniformly translated into `KeyMaterialException`.

| Format | Import | Export |
|---|---|---|
| PKCS#8 (private, DER) | `ImportPkcs8` | `ExportPkcs8` |
| SubjectPublicKeyInfo (public, DER) | `ImportSubjectPublicKeyInfo` | `ExportSubjectPublicKeyInfo` |
| X.509 certificate | `ImportPublicKeyFromCertificate` (key only, **no** chain check) | — |
| PEM | `ImportFromPem` (private/public auto.) | `ExportPublicKeyPem`, `ExportPkcs8Pem` |
| EBICS `RSAKeyValue` (modulus/exponent) | `ImportRsaKeyValue` | `ExportRsaKeyValue` |

```csharp
var material = RsaKeyImportExport.ImportPkcs8(pkcs8Der);   // HasPrivateKey == true
var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(material);
RsaKeyImportExport.ExportPkcs8(material.ToPublicOnly());   // KeyMaterialException (no private key)
```

## EBICS version relation

Key roles (A/E/X) and the RSA basis are identical across H003/H004/H005; only the
**permitted versions** differ (see table above) and reside centrally in
`KeyVersions`. The `RSAKeyValue` bytes (modulus/exponent) correspond to the shared,
version-independent binding `XmlDsig.RsaKeyValueType`.

## Tests

`tests/EBICO.Tests/Crypto/` (Tier A, CI-safe, without proprietary samples):

- `KeyPurposeTests` — letter mapping A/E/X, rejection of unknown letters.
- `KeyVersionTests` — form validation, purpose derivation, well-formed-but-unknown, `default` caveat.
- `KeyVersionsTests` — registry content/order, `Get`/`TryGet`, permission table,
  `EnsurePermitted`/`PermittedFor`/`Default`, legacy/padding metadata.
- `RsaKeyMaterialTests` — public/private, minimum size, canonical modulus form, defensive copying.
- `RsaKeyImportExportTests` — round-trip fidelity (PKCS#8/SPKI/PEM/`RSAKeyValue`), cross-format,
  certificate extraction, error cases (malformed/EC certificate/too small) **and** a fixed,
  externally generated known-answer vector to safeguard canonicalization.

## Related

- [Bank-technical signature A005/A006](bank-signature.md) — the first crypto operation that builds on this layer (#19)
- [ADR-0008 — Crypto library](../adr/0008-crypto-library.md)
- [ADR-0007 — Domain value objects as `readonly record struct`](../adr/0007-domain-value-objects-record-struct.md) — pattern for `KeyVersion`
- [Version dispatch](version-dispatch.md) — the `EbicsVersion` registry that `KeyVersions` refers to
- [XSD bindings](xsd-bindings.md) — `RsaKeyValueType` and the (to-be-bound-later) `PubKeyInfoType` types
