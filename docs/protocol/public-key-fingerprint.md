# Public-key fingerprints (hash values of public keys) (H003/H004/H005)

The EBICS **public-key fingerprint** in `EBICO.Core` (`Crypto/`): the **SHA-256 hash value**
of a public RSA key. It is needed in three places — in the **INI letter**
(human visual check of the transferred key at the bank), in the **HPB response**
(the client verifies the returned bank keys) and in the **`BankPubKeyDigests`**
of the request header (the server verifies the hashes of the bank keys known to it that the
client sends along). Builds on the key layer from [#18](key-representation.md) and uses the
SHA-256 building block established in [#19](bank-signature.md). Issue **#22** (Milestone M2),
crypto library: [ADR-0008](../adr/0008-crypto-library.md)
(`System.Security.Cryptography`, no BouncyCastle) — SHA-256 comes natively from the BCL.

> **Scope:** Deliberately only the **byte level** — compute the fingerprint (`Compute`), verify it
> constant-time against a transmitted hash (`Verify`) and provide the hex representation for the
> INI letter (`ToLetterFormat`). The **XML assembly** of the digest elements
> (`PubKeyDigestType`, `StaticHeaderTypeBankPubKeyDigests`, `EncryptionPubKeyDigest`), the
> INI/HIA/HPB order-data binding and the **full INI-letter document** (subscriber IDs,
> key versions, date, signature line) do **not** belong here — they are the concern of the
> dispatch/onboarding layer (M3) or the Suite (M7). The `EncryptionPubKeyDigest` building block
> deferred from [#21](encryption-e002.md) is resolved here via the two ingredients the
> DTO needs: `Compute(...)` (the `Value` bytes) and `DigestAlgorithm` (the `@Algorithm` string).
> The literal DTO population is then a three-liner in M3:
>
> ```csharp
> new Schema.H004.DataEncryptionInfoTypeEncryptionPubKeyDigest {
>     Value     = PublicKeyFingerprint.Compute(bankEncKey),
>     Algorithm = PublicKeyFingerprint.DigestAlgorithm,
>     Version   = "E002",
> };
> ```

## Building blocks

Under `src/EBICO.Core/Crypto/` (namespace `EBICO.Core.Crypto`):

| Building block | Location | Purpose |
|---|---|---|
| `PublicKeyFingerprint` (static) | `PublicKeyFingerprint.cs` | compute the fingerprint (`Compute`), build the hash input (`BuildHashInput`), verify constant-time (`Verify`), render INI-letter hex (`ToLetterFormat`); constants `HashAlgorithm` (SHA-256) and `DigestAlgorithm` (wire `@Algorithm` URI) |

Reused from [#18](key-representation.md): `RsaKeyMaterial` (`Modulus`/`Exponent` in
canonical form, `ToPublicOnly()`), `RsaKeyImportExport` (`ImportRsaKeyValue`,
`ImportPublicKeyFromCertificate`) as well as `KeyMaterialException`. The constant-time comparison
(`CryptographicOperations.FixedTimeEquals`) follows the pattern of the X002 signature
([#20](auth-signature-x002.md)).

## Fingerprint — procedure

The hash value is formed over an **ASCII string** of exponent and modulus. Each of
the two is represented as **hex** (leading zeros removed), they are separated — exponent first —
by **a single space**, and **SHA-256** runs over these ASCII bytes. The
input bytes come from `RsaKeyMaterial.Exponent`/`.Modulus`, which are already **canonical**
(unsigned big-endian, without a leading null byte), so that the fingerprint sees the same bytes as
the order-data layer.

| Step | Input/output | BCL |
|---|---|---|
| Hex per number | `Exponent`/`Modulus` (bytes) → lowercase hex, leading null **nibbles** stripped | `Convert.ToHexStringLower(...).TrimStart('0')` |
| Hash input | `"<exponent-hex> <modulus-hex>"` (ASCII) | `Encoding.ASCII.GetBytes(...)` |
| Fingerprint | 32-byte SHA-256 digest | `SHA256.HashData(...)` |
| Wire (`PubKeyDigestType/@Value`) | digest → base64 | XML serialization (M3) |
| INI letter | digest → grouped uppercase hex | `ToLetterFormat(...)` |

```csharp
// Fingerprint of any public key (version-agnostic):
byte[] digest = PublicKeyFingerprint.Compute(bankKey);   // 32 Byte SHA-256

// Verification of a hash sent by the counterparty (constant-time, no throw):
bool ok = PublicKeyFingerprint.Verify(bankKey, clientSentDigest);

// Representation for the INI letter:
string letter = PublicKeyFingerprint.ToLetterFormat(digest);
```

For the exponent 65537 (`0x010001`) the nibble strip yields the hex string `10001` — not
`010001` — and the hash input accordingly begins with `10001 …`.

> **⚠️ Spec caveat:** The exact formatting of the hash input — **order**
> exponent-before-modulus, **hex notation** (lowercase) and the **stripping of leading
> null nibbles** plus the single-space separator — are EBICS spec details that are **not yet
> verified against the official schemas/annexes** (cf. CLAUDE.md). They are confined to the single
> place `NormalizeHashInput` and are caught up there as soon as the spec is available. Since
> `Compute` **and** `BuildHashInput` run through this place, a changeover is a
> single-place change. Likewise the `DigestAlgorithm` URI
> (`http://www.w3.org/2001/04/xmlenc#sha256`, identical to the X002 `DigestMethodAlgorithm`) is
> encapsulated as a constant.

## INI-letter representation

`ToLetterFormat` renders the 32-byte digest as **uppercase hex**, byte pairs separated by a
single space, **8 bytes per line** — i.e. four lines for SHA-256:

```
73 16 CA CB 34 AD CD 7D
A8 2B 17 32 AB F5 0B D0
67 AB 7C 14 40 3F 88 28
A1 06 8D BE 04 2D 77 F1
```

The grouping is purely **cosmetic** (the visual check by the bank employee) and is **not** a
spec caveat: the wire uses base64 of the raw bytes, the printed letter uses hex. Whoever needs
ungrouped hex uses `Convert.ToHexString(digest)` directly.

## Error behaviour

| Condition | Behaviour |
|---|---|
| `key == null` (in `Compute`/`BuildHashInput`/`Verify`) | `ArgumentNullException` |
| `Verify` with a wrong digest (content differs) | `false` |
| `Verify` with a differing length (truncated/overlong) | `false` (via `FixedTimeEquals`) |

> **`false`-instead-of-throw in `Verify`:** like `BankSignature.Verify`, a bad,
> client-sent digest yields a clean rejection (`false`), not an exception — only `key == null`
> throws. Fingerprints are **not secret**, the constant-time comparison is not
> security-critical here, but follows the project convention from [#20](auth-signature-x002.md).

## EBICS version relation

The fingerprint is **version-agnostic**: it never touches XML and sees exclusively an
`RsaKeyMaterial`. The protocol version decides only **where** this material comes from:

| Version | Wire source of the public key | Path to `RsaKeyMaterial` |
|---|---|---|
| H003 / H004 | `PubKeyInfoType/PubKeyValue/RSAKeyValue` (modulus/exponent, base64) | `RsaKeyImportExport.ImportRsaKeyValue(mod, exp)` |
| H005 | `PubKeyInfoType/X509Data` (certificate, **no** `PubKeyValue`) | `RsaKeyImportExport.ImportPublicKeyFromCertificate(cert)` |

EBICS 3.0 (H005) is certificate-based; there the public RSA key is read from the
certificate. In both cases the import yields **the same canonical modulus/
exponent bytes** and thus **the same** fingerprint. The mapping XML → `RsaKeyMaterial` belongs
in the dispatch/onboarding layer (M3), not in this primitive.

## Tests

`tests/EBICO.Tests/Crypto/PublicKeyFingerprintTests.cs` (Tier A, CI-safe, without proprietary
samples; the same fixed 2048-bit key as in `BankSignatureTests`/`EncryptionE002Tests`):

- **Known-answer vectors** (deterministic, byte-exactly pinned): the fingerprint digest, the
  ASCII hash input (isolates the normalisation seam from SHA-256) and the INI-letter hex form.
- **Nibble strip:** the hash input begins with `10001 ` (exponent 65537), not `010001`.
- Happy path / self-consistency: 32-byte length, determinism, `ToPublicOnly()` == key pair,
  as well as **version equivalence** (fingerprint via certificate == via raw RSA key).
- Verify negative cases: wrong digest, truncated digest, foreign key, `null` key.

## Related

- [Key pairs & representation (A/E/X)](key-representation.md) — the underlying key layer (#18)
- [Bank-technical signature A005/A006](bank-signature.md) — shares the SHA-256 building block (#19)
- [Encryption E002](encryption-e002.md) — deferred the `EncryptionPubKeyDigest` building block here (#21)
- [ADR-0008 — Crypto library](../adr/0008-crypto-library.md)
