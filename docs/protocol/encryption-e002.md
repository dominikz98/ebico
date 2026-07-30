# Encryption E002 (RSA-OAEP + AES-128-CBC) (H003/H004/H005)

The EBICS transport encryption in `EBICO.Core` (`Crypto/`): a **hybrid** procedure —
the order data is encrypted symmetrically with a one-time **transaction key** (AES-128-CBC),
and this transaction key is encrypted asymmetrically with the recipient's public
encryption key (`E`) via **RSAES-OAEP over SHA-256**.
Builds on the key layer from [#18](key-representation.md). Issue **#21** (Milestone M2),
crypto library: [ADR-0008](../adr/0008-krypto-bibliothek.md)
(`System.Security.Cryptography`, no BouncyCastle).

> **Scope:** Deliberately only the **byte level** of the hybrid encryption — the two
> ciphertexts (encrypted transaction key + encrypted order data). The
> `DataEncryptionInfo`/`EncryptionPubKeyDigest` XML assembly (#22), the segmentation/the
> `DataTransfer` envelope, the X002 authentication signature (#20) and the bank-technical
> signature that provides integrity/authenticity (#19) do **not** belong here. This
> layer only provides the ciphertext bytes that those layers assemble onto
> `DataEncryptionInfoType`.

## Building blocks

Under `src/EBICO.Core/Crypto/` (namespace `EBICO.Core.Crypto`):

| Building block | Location | Purpose |
|---|---|---|
| `EncryptionE002` (static) | `EncryptionE002.cs` | generate transaction key, RSA-OAEP over the key, AES-128-CBC over the data, combined hybrid flow (stateless BCL wrappers) |
| `EncryptedOrderData` (record struct) | `EncryptionE002.cs` | result of `Encrypt`: encrypted transaction key + encrypted order data |

Reused from [#18](key-representation.md): `RsaKeyMaterial` (`CreateRsa()`,
`HasPrivateKey`, `ToPublicOnly()`), the `KeyVersions` registry (`TryGet`, `PaddingIntent`),
`KeyPurpose` as well as `KeyMaterialException`.

## E002 — procedure

EBICS encrypts the order data **symmetrically** with a random one-time key
(AES-128) in CBC mode and encrypts this key **asymmetrically** with the recipient's public
`E` key. The RSA padding variant depends on the key version and is resolved
**registry-driven** from `KeyVersionInfo.PaddingIntent` (not hard-coded):

| Step | Scheme | BCL | Determinism |
|---|---|---|---|
| Transaction key | AES-128 (16 bytes), random | `RandomNumberGenerator.GetBytes(16)` | randomised |
| Key encryption | RSAES-OAEP over SHA-256 | `RSAEncryptionPadding.OaepSHA256` | randomised |
| Order data | AES-128-CBC, PKCS7 padding, null IV | `Aes.EncryptCbc(data, ivZero, PKCS7)` | deterministic (same key/IV/plaintext → same ciphertext) |

OAEP-SHA256 on a 2048-bit key holds up to 190 bytes of plaintext — a 16-byte AES key
fits comfortably. RSA-OAEP runs over `RSA.Encrypt`/`RSA.Decrypt`, the AES layer over
`Aes.EncryptCbc`/`Aes.DecryptCbc`.

```csharp
// Full hybrid (the usual layer):
var enc = EncryptionE002.Encrypt(orderData, recipientPubKey, KeyVersion.Create("E002"));
byte[] back = EncryptionE002.Decrypt(enc, recipientKey, KeyVersion.Create("E002"));

// The primitives individually (e.g. when a transaction key is reused across segments):
var tk        = EncryptionE002.GenerateTransactionKey();                 // 16-byte AES key
var encData   = EncryptionE002.EncryptOrderData(orderData, tk);          // AES-128-CBC
var encTk     = EncryptionE002.EncryptTransactionKey(tk, recipientPubKey, KeyVersion.Create("E002")); // RSA-OAEP
var tkBack    = EncryptionE002.DecryptTransactionKey(encTk, recipientKey, KeyVersion.Create("E002"));
var dataBack  = EncryptionE002.DecryptOrderData(encData, tkBack);
```

`GenerateTransactionKey` and the two primitive pairs are **public**, because the full hybrid
is not byte-exactly pinnable due to the randomised RSA-OAEP; only the deterministic AES layer
can be anchored with a fixed key as a known-answer vector.

## IV & padding — spec caveat

> **⚠️ Spec caveat (symmetric):** The **null IV** (16 null bytes) and the **PKCS7 padding**
> of the order-data encryption are EBICS spec details that are **not yet verified against the
> official schemas/annexes** (cf. CLAUDE.md). They are confined to a single place
> (`TransactionIv`, `SymmetricPadding`) and are caught up there as soon as the spec is available.
> Since **both** `EncryptOrderData` and `DecryptOrderData` run through this place, internally
> consistent encrypt-→-decrypt round-trips remain unaffected.

> **⚠️ Spec caveat (RSA padding):** The EBICS version `E002` encrypted the transaction key in
> some historical spec revisions with **RSAES-PKCS1-v1_5** instead of OAEP. EBICO
> follows the registry intention (**OAEP-SHA256**, as required in this issue). The padding comes
> from `KeyVersions` (`E002 → RsaPaddingScheme.Oaep`) and is **never** hard-coded in the primitive —
> should real bank interop require PKCS1-v1.5, that is a **one-line change in
> `KeyVersions.cs`**, not an intervention in `EncryptionE002`. ADR-0008 already anticipates this revision.

## Error behaviour

| Condition | Behaviour |
|---|---|
| `key == null` / `recipientKey == null` | `ArgumentNullException` |
| Decrypting (transaction key) without a private key | `KeyMaterialException` |
| `version` not a known **encryption** version with OAEP (`A005`, `X002`, `E001`, `A999`, `default`) | `InvalidOperationException` |
| Transaction-key length ≠ 16 bytes | `ArgumentException` |
| Decrypting with the wrong key / tampered RSA ciphertext | `CryptographicException` (OAEP integrity check) |
| Decrypting a tampered AES ciphertext (last block) | `CryptographicException` (PKCS7 padding invalid) |

> **No `false`-instead-of-throw path:** Unlike `BankSignature.Verify`, encryption/decryption has
> no boolean verdict — **every** error throws. CBC provides **no integrity**: an AES ciphertext
> tampered in an earlier block yields corrupted but "validly padded" plaintext
> without an exception. Integrity/authenticity is provided by the bank-technical signature (#19), not E002.

> **No version-permission check here:** Whether E002 is permitted with an EBICS protocol version
> remains the task of `KeyVersions.EnsurePermitted` in the dispatch/onboarding layer. This
> primitive stays policy-free and does **not** raise a `KeyVersionNotPermittedException`.

## EBICS version relation

The procedure (AES-128-CBC + RSA-OAEP transaction key) is identical across H003/H004/H005;
E002 is permitted in all three (centrally in [`KeyVersions`](key-representation.md)). The
legacy version E001 (RSAES-PKCS1-v1.5) is **not** the target of this issue — it is deliberately
rejected by the `ResolveEncryptionPadding` place (`InvalidOperationException`).

## Tests

`tests/EBICO.Tests/Crypto/EncryptionE002Tests.cs` (Tier A, CI-safe, without proprietary samples):

- Happy path: full hybrid Encrypt → Decrypt, RSA-OAEP primitive round-trip, AES primitive round-trip.
- Encrypting with `ToPublicOnly()` → decrypting with a key pair (private key not needed for
  encryption).
- `Encrypt` yields both ciphertexts; encrypted transaction key == 256 bytes (2048-bit modulus).
- **Deterministic AES-128-CBC known-answer vector**: fixed 16-byte key + fixed
  order data → byte-identical ciphertext (pins null IV and PKCS7), plus the decrypt direction.
- **RSA-OAEP non-determinism**: encrypt twice → different ciphertexts, both
  decrypt; OAEP round-trip with a fixed PKCS#8 key.
- Negative cases: decrypting without a private key, wrong key, tampered RSA or
  AES ciphertext, wrong key length, `null` key, wrong/unknown version
  (`A005`/`X002`/`E001`/`A999`/`default`).

## Related

- [Key pairs & representation (A/E/X)](key-representation.md) — the underlying key layer (#18)
- [Bank-technical signature A005/A006](bank-signature.md) — the sibling crypto operation, provides the integrity (#19)
- [ADR-0008 — Crypto library](../adr/0008-krypto-bibliothek.md)
