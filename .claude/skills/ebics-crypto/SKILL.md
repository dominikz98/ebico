---
name: ebics-crypto
description: >-
  Reference and extension guide for the EBICS cryptography in EBICO.Core (namespace
  EBICO.Core.Crypto). Use when working on the bank-technical signature (A005/A006), the authentication
  signature (X002), hybrid encryption (E002), public key fingerprints, X.509 certificate verification
  or the A/E/X key representation. Covers the procedures, the registry-driven padding mapping and
  the version differences H003/H004 (RSAKeyValue) vs. H005 (X.509).
---

# EBICS crypto (A00x / X002 / E002)

The crypto primitives live in `src/EBICO.Core/Crypto` and build exclusively on
`System.Security.Cryptography` (ADR-0008, no third-party crypto library). Before any change, read the
matching protocol doc under `docs/protocol/` — that is where the binding procedure including
spec caveats against the annexes is written down.

## The procedures at a glance

- **Bank-technical signature A005/A006** (`docs/protocol/bank-signature.md`): order hash SHA-256,
  signing/verifying A005 = RSA **PKCS#1 v1.5**, A006 = RSA **PSS**. Padding is registry-driven by
  key version. *Note:* ES/A00x signature verification of the order data remains deferred on the
  server side (spec caveat) — keep that in mind when extending.
- **Authentication signature X002** (`docs/protocol/auth-signature-x002.md`): XML-DSig `AuthSignature`
  over all `authenticate="true"` nodes. Reference digest SHA-256 + `SignatureValue` RSA-PKCS#1 v1.5,
  document-context C14N **inclusive**. Active on the server side (`X002EbicsRequestVerifier`, ADR-0023).
- **Encryption E002** (`docs/protocol/encryption-e002.md`): hybrid — AES-128-CBC over the
  order data, RSAES-**OAEP-SHA256** over the transaction key. Type `EncryptionE002`.
- **Public key fingerprints** (`docs/protocol/public-key-fingerprint.md`): SHA-256 over exponent+modulus,
  representation for the INI letter and the HPB response; verify client-sent hashes in **constant time**
  (`PublicKeyFingerprint.Verify`).
- **X.509 verification** (`docs/protocol/certificate-verification-x509.md`): chain/trust anchor
  (configurable, test CA), validity, KeyUsage per key role; key-only procedures
  (H003/H004) as a policy (`CertificateRequirement`).

## Cross-cutting rules

- **Registry-driven padding mapping:** the padding (v1.5/PSS/OAEP) hangs off the key version
  (`KeyVersion`), not off the call site — keep the mapping central, do not duplicate it.
- **Version representation** (`docs/protocol/key-representation.md`): H003/H004 transport keys as
  `RSAKeyValue`, H005 as X.509. Key roles A (signature) / E (enc, E002) / X (auth, X002).
  RSA container via `RsaKeyMaterial` (RSA-2048 as the practical lower bound in tests).
- **The canonical modulus form applies on import as well** (#117, ADR-0029): `ds:Modulus` is a
  `CryptoBinary` without a leading zero, yet real clients still send the ASN.1 INTEGER form with a
  sign byte. `RsaKeyMaterial` trims before importing — otherwise a 2056-bit RSA instance is created
  whose padding operations fail. Do not undo this when touching key import/export.
- **The version permission table** (`KeyVersions`) is the single place for "which key version applies
  in which protocol version" — `A006`/PSS applies to **H004 and H005**, not to H003.
- **Determinism/C14N:** serialised output must be stable (`docs/protocol/serialization-c14n.md`),
  because signature/digest rest on it.

## Definition of Done

Test vectors + sample XML instead of self-consistency (`tests/EBICO.Tests/Crypto`), update the docs,
ADR if applicable. Process: skill `ebics-feature-workflow`.

## Sources

- Code: `src/EBICO.Core/Crypto`, `src/EBICO.Core/ReturnCodes` (`EbicsReturnCode`/`EbicsReturnCodes`).
- Docs: `docs/protocol/bank-signature.md`, `docs/protocol/auth-signature-x002.md`,
  `docs/protocol/encryption-e002.md`, `docs/protocol/public-key-fingerprint.md`,
  `docs/protocol/certificate-verification-x509.md`, `docs/protocol/key-representation.md`,
  `docs/protocol/serialization-c14n.md`. ADR: 0008 (crypto library), 0023 (X002 verification).
