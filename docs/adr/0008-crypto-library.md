# 0008 — Crypto library: `System.Security.Cryptography`

- Status: accepted
- Date: 2026-06-22

## Context

Milestone **M2 (crypto)** begins EBICO's cryptographic layer. Issue **#18** (key
pairs & representation) is the first crypto task; the following issues build on it:
A005/A006 signature (#19), X002 authentication signature (#20), E002 encryption with
RSA + AES (#21), hashing/public-key fingerprints (#22) and X.509 certificate
verification (#23).

EBICS uses RSA throughout: signature versions A004/A005 (RSASSA-PKCS1-v1_5) and A006
(RSASSA-PSS), authentication X001/X002 (RSASSA-PKCS1-v1_5), encryption E002
(RSAES-OAEP for the transaction key, AES-128-CBC for the order data) as well as
SHA-256 for hashes/fingerprints. Key and certificate exchange happens via PKCS#8
(private keys) and X.509/SubjectPublicKeyInfo (public keys).

The ADR backlog left open whether the BCL (`System.Security.Cryptography`) suffices
for this or an external package (BouncyCastle) is needed. So far `EBICO.Core`
references only `System.Security.Cryptography.Xml` (for C14N, #15); the test
infrastructure (`TestCertificates`) already generates RSA keys/certificates
in-process with the BCL.

## Decision

For all cryptographic operations in M2, EBICO uses **`System.Security.Cryptography`**
from the .NET framework exclusively. **No** additional crypto package (in particular
no BouncyCastle) is taken on.

For issue #18 the BCL covers all requirements directly:

- **Key model:** `RSAParameters` (modulus/exponent + private components).
- **Import/export:** `RSA.ImportPkcs8PrivateKey`/`ExportPkcs8PrivateKey`,
  `ImportSubjectPublicKeyInfo`/`ExportSubjectPublicKeyInfo`, `ImportFromPem` as well
  as the PEM exports; `X509Certificate2.GetRSAPublicKey()` for extracting the key
  from certificates.
- **EBICS `RSAKeyValue`:** maps directly to `RSAParameters.Modulus`/`.Exponent`.

## Consequences

- No additional dependency, no license/supply-chain questions, smaller build matrix.
- Consistency with the BCL already in use (tests, C14N).
- The later M2 operations are also covered natively: `RSASignaturePadding.Pss` (A006),
  `RSASignaturePadding.Pkcs1` (A004/A005, X002), `RSAEncryptionPadding.OaepSHA256`
  (E002), `Aes` and `SHA256`.
- **Risk/revision:** should a concrete interop gap with a real bank setup arise in
  #21/#23 (e.g. exotic certificate or OAEP parameterisation), this ADR is
  re-evaluated; the preference would then be a narrowly scoped dependency rather than
  a blanket library switch.

## Alternatives

- **BouncyCastle:** broader algorithm range and finer control over encodings, but an
  external dependency that #18 (and, foreseeably, M2) does not need — rejected until
  a concrete need arises.
- **Mixed mode (BCL + BouncyCastle selectively):** increases complexity without
  current benefit — rejected; remains mentioned as a fallback option in the risk
  section.
