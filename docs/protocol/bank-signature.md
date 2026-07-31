# Bank-technical signature A005/A006 (H003/H004/H005)

The first real crypto **operation** in `EBICO.Core` (`Crypto/`): creating and verifying the
bank-technical (authorising) signature over order data — key version **A005**
(RSASSA-PKCS1-v1.5) and **A006** (RSASSA-PSS), both over **SHA-256**. Builds on the key layer
from [#18](key-representation.md). Issue **#19** (Milestone M2), crypto library:
[ADR-0008](../adr/0008-crypto-library.md)
(`System.Security.Cryptography`, no BouncyCastle).

> **Scope:** Deliberately only the **byte level** of the RSA signature plus the order hash
> (SHA-256). The X002 authentication signature (#20), the encryption E002 (#21),
> hashing/public-key fingerprints (#22) and the X.509 chain check (#23) do **not** belong
> here. The XML-DSig `SignedInfo` envelope, the C14N of the signed XML and the
> `UserSignatureData`/`OrderSignatureData` container assembly are likewise deferred to the
> order-data/transaction issues — this layer only provides the signature bytes and the hash
> that those layers assemble.

## Building blocks

Under `src/EBICO.Core/Crypto/` (namespace `EBICO.Core.Crypto`):

| Building block | Location | Purpose |
|---|---|---|
| `BankSignature` (static) | `BankSignature.cs` | Order hash, signing/verifying A005/A006 (stateless BCL wrappers) |

Reused from [#18](key-representation.md): `RsaKeyMaterial` (`CreateRsa()`,
`HasPrivateKey`, `ToPublicOnly()`), the `KeyVersions` registry (`TryGet`, `PaddingIntent`),
`KeyPurpose` as well as `KeyMaterialException`.

## A005 / A006 — procedure

EBICS forms the bank-technical signature over a **SHA-256 order hash** of the order data
and signs it with the private signature key (`A`). The padding variant depends on the
key version and is resolved **registry-driven** from `KeyVersionInfo.PaddingIntent`
(not hard-coded):

| Version | RSA scheme | BCL padding | Determinism |
|---|---|---|---|
| A005 | RSASSA-PKCS1-v1.5 | `RSASignaturePadding.Pkcs1` | deterministic (same input → same signature) |
| A006 | RSASSA-PSS | `RSASignaturePadding.Pss` | randomised (random salt) |

PSS uses the BCL default (salt length = hash length = 32 bytes, MGF1-SHA-256), which matches
the A006 expectation. Both run over `RSA.SignHash`/`RSA.VerifyHash` with
`HashAlgorithmName.SHA256`.

```csharp
var hash = BankSignature.ComputeOrderHash(orderData);          // 32-Byte SHA-256-Digest
var sig  = BankSignature.Sign(orderData, signerKey, KeyVersion.Create("A005"));
bool ok  = BankSignature.Verify(orderData, sig, signerPubKey, KeyVersion.Create("A005"));

// Hash explicitly (e.g. when the hash is already available elsewhere):
var sig2 = BankSignature.SignHash(hash, signerKey, KeyVersion.Create("A006"));
bool ok2 = BankSignature.VerifyHash(hash, sig2, signerPubKey, KeyVersion.Create("A006"));
```

`ComputeOrderHash` is **public**, so that the order-data layer and the fingerprint layer (#22)
use exactly the same bytes (same rationale as the canonical modulus/exponent in #18).

## Order hash & normalisation

> **⚠️ Spec caveat:** The exact **normalisation** of the order data before hashing (e.g.
> line-ending normalisation for certain formats) is an EBICS spec detail that is **not yet
> verified against the official schemas/annexes** (cf. CLAUDE.md). It is confined to a single
> place (`NormalizeOrderData`, currently identity/pass-through) and is caught up there as soon as
> the spec is available. Since **both** `Sign` and `Verify` run through this place, internally
> consistent sign-→-verify round-trips remain unaffected.

## Error behaviour

| Condition | Behaviour |
|---|---|
| `key == null` (Sign/Verify) | `ArgumentNullException` |
| Signing without a private key | `KeyMaterialException` |
| `version` not a known **signature** version (`A999`, `E002`, `X002`, `default`) | `InvalidOperationException` |
| Verify: wrong key / tampered data / tampered or too-short signature | returns `false` (does **not** throw) |

> **No version-permission check here:** Whether a version is permitted with an EBICS protocol
> version (e.g. A006 with H003) remains the task of `KeyVersions.EnsurePermitted` in the
> dispatch/onboarding layer. This primitive stays policy-free and does **not** raise a
> `KeyVersionNotPermittedException`. The `false`-instead-of-throw path during verification keeps
> the server robust: a faulty client signature is a clean rejection, not a crash.

## EBICS version relation

The procedure (SHA-256 order hash + RSA signature) is identical across H003/H004/H005; only the
**permitted versions** differ (A006 from EBICS 2.5/H004 on, see #117 and
[ADR-0029](../adr/0029-interop-fixes-real-clients.md)) and reside centrally in
[`KeyVersions`](key-representation.md). A004 (legacy) is covered by the same PKCS1-v1.5 mapping,
but is not the target of this issue.

## Tests

`tests/EBICO.Tests/Crypto/BankSignatureTests.cs` (Tier A, CI-safe, without proprietary samples):

- Happy path A005 and A006 (Sign → Verify).
- Round-trip over an explicit hash; `ComputeOrderHash` length == 32.
- Cross-verify with `ToPublicOnly()` (private key not needed for verification).
- Negative cases: tampered data, tampered signature, too-short signature, wrong key,
  wrong/unknown version (`E002`/`X002`/`A999`/`default`), signing without a private key,
  `null` key, cross-version (A005 signature verified as A006 and vice versa).
- **Deterministic A005 known-answer vector**: fixed PKCS#8 key (the same as in
  `RsaKeyImportExportTests`) + fixed order data → byte-identical signature (pins padding and
  normalisation).
- **A006/PSS non-determinism**: sign twice → different signatures, both verify.

## Related

- [Key pairs & representation (A/E/X)](key-representation.md) — the underlying key layer (#18)
- [ADR-0008 — Crypto library](../adr/0008-crypto-library.md)
