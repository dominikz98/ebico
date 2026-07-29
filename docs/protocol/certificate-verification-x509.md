# Certificate verification (X.509) (H005)

The **X.509 certificate verification** in `EBICO.Core` (`Crypto/`): it checks a subscriber or
bank certificate against a **configurable trust anchor** (chain/test CA), controls
**validity** (time range) and **usage purpose** (KeyUsage matching the EBICS key role) and
optionally binds the certificate to a known subscriber key. EBICS **3.0 / H005 is
certificate-based** — there the public RSA key is read from an X.509 certificate
(`PubKeyInfoType/X509Data`); **H003/H004 use pure RSA keys** (trust via the
INI-letter fingerprint, [#22](public-key-fingerprint.md)). Builds on the key layer from
[#18](key-representation.md). Issue **#23** (Milestone M2), crypto library:
[ADR-0008](../adr/0008-krypto-bibliothek.md) (`System.Security.Cryptography.X509Certificates`,
no BouncyCastle) — `X509Chain`/`X509ChainPolicy` come natively from the BCL.

> **Scope:** This primitive checks a **single, fully loaded** `X509Certificate2`
> against options. The **XML extraction** of the certificate from `X509Data`, the mapping
> certificate↔subscriber and the question of **which** trust anchor applies per bank belong in the
> dispatch/onboarding layer (M3) or the Suite (M7) — not here. The decision **whether**
> a certificate is required at all is a version/onboarding property and is modelled via
> `CertificateRequirement` / `CertificateRequirements.For(version)`.

## Building blocks

Under `src/EBICO.Core/Crypto/` (namespace `EBICO.Core.Crypto`):

| Building block | Location | Purpose |
|---|---|---|
| `X509CertificateVerifier` (static) | `X509CertificateVerifier.cs` | `Verify(cert, options)` (+ convenience overload): build/check chain, validity, key usage, optional key binding; encapsulated `ExpectedKeyUsage` mapping |
| `CertificateVerificationOptions` | `CertificateVerificationOptions.cs` | configurable: `TrustAnchors`, `ExtraStore`, `TrustMode`, `RevocationMode`/`Flag`, `VerificationFlags`, `VerificationTime`, `ExpectedPurpose`, `ExpectedPublicKey` |
| `CertificateVerificationResult` | `CertificateVerificationResult.cs` | result: `IsValid`, `Errors` (`[Flags]`), raw `ChainStatus`, `Diagnostics` |
| `CertificateVerificationError` (`[Flags]`) | `CertificateVerificationResult.cs` | rejection reasons, reportable together |
| `CertificateRequirement` / `CertificateRequirements` | `CertificateRequirement.cs` | policy "certificate needed?" per EBICS version (H003/H004 → `NotUsed`, H005 → `Required`) |

Reused from [#18](key-representation.md): `RsaKeyMaterial` (canonical modulus/exponent for
the key binding), `RsaKeyImportExport.ImportPublicKeyFromCertificate`. The `Verify` convention
(clean result instead of exception) follows [#19](bank-signature.md)/[#22](public-key-fingerprint.md).

## Procedure

`Verify` builds the chain via `X509Chain` and derives the overall verdict from the **mapped**
reasons — not from the `Build()` bool. So, for example, with `RevocationMode.NoCheck` a missing
revocation response does **not** fail the check.

| Step | Input/output | BCL |
|---|---|---|
| Trust/chain | anchors from `TrustAnchors` (→ `CustomRootTrust`), intermediates from `ExtraStore` | `X509ChainPolicy.CustomTrustStore` / `.ExtraStore` |
| Point in time | `VerificationTime` (UTC-pinned), otherwise "now" | `X509ChainPolicy.VerificationTime` |
| Offline | no AIA/CRL/OCSP network calls | `DisableCertificateDownloads = true`, `RevocationMode.NoCheck` |
| Validity | leaf `NotBefore`/`NotAfter` against the point in time (in UTC) | `X509Certificate2.NotBefore/NotAfter` |
| Key usage | `ExpectedPurpose` → expected `X509KeyUsageFlags` | `X509KeyUsageExtension.KeyUsages` |
| Key binding | `ExpectedPublicKey` vs. cert RSA (canonical) | `GetRSAPublicKey()` + `RsaKeyMaterial` |

**Chain/trust:** `X509ChainStatusFlags` are aggregated and mapped:
`UntrustedRoot`/`PartialChain` → `UntrustedRoot`; `NotTimeValid` → `NotTimeValid`;
`Revoked` → `Revoked`; `RevocationStatusUnknown`/`OfflineRevocation` → `RevocationStatusUnknown`;
`NotSignatureValid` → `InvalidSignature`; `InvalidBasicConstraints` → likewise;
`NotValidForUsage`/`HasNotSupportedCriticalExtension` → `InvalidKeyUsage`; everything else → `Other`.

**Validity:** In addition to the chain time check, the verifier refines at the **leaf** into `Expired`
(point in time after `NotAfter`) or `NotYetValid` (point in time before `NotBefore`) — both also set
`NotTimeValid`.

**Usage purpose:** If `ExpectedPurpose` is set, the KeyUsage extension is checked. If the extension is
missing, this counts as `InvalidKeyUsage` (strict).

```csharp
using var ca = /* Vertrauensanker / Bank-CA */;
using var cert = /* aus X509Data geladenes Teilnehmerzertifikat */;

var result = X509CertificateVerifier.Verify(cert, TrustStore(ca), KeyPurpose.Signature);
if (!result.IsValid)
{
    // result.Errors ist ein [Flags]-Wert; result.Diagnostics liefert lesbare Texte.
}
```

### Key-usage mapping

| `KeyPurpose` | required (AllOf) | one of (AnyOf) |
|---|---|---|
| `Signature` | `DigitalSignature` | — (`NonRepudiation` allowed, not enforced) |
| `Authentication` | `DigitalSignature` | — |
| `Encryption` | — | `KeyEncipherment` \| `DataEncipherment` |

> **⚠️ Spec caveat:** The **EBICS certificate profile** (KeyUsage per key role), the
> **strict** handling of missing KeyUsage extensions, the **revocation default** (`NoCheck`) and
> the **version requirement** (`CertificateRequirements.For`) are not yet verified against the
> **official EBICS schemas/annexes** (cf. CLAUDE.md). They are encapsulated in **one place** each
> — `X509CertificateVerifier.ExpectedKeyUsage` and `CertificateRequirements.For` respectively — and
> are caught up there as soon as the spec is available. **Extended Key Usage (EKU)** is deliberately
> **not** checked (EBICS defines no standard EKU OIDs for A/E/X keys); this remains a
> documented opt-in extension point via `ChainPolicy.ApplicationPolicy`.

## Error behaviour

| Condition | Behaviour |
|---|---|
| `certificate == null` / `options == null` / `trustAnchors == null` | `ArgumentNullException` |
| well-formed but invalid certificate (untrusted/expired/wrong usage/…) | `IsValid == false`, matching `Errors` bits, **no** throw |
| non-RSA certificate (e.g. ECDSA) | `Errors` contains `NotRsa` (clean rejection, no throw) |
| several defects at once | all reasons together in `Errors` (`[Flags]`) |

> **Result instead of throwing:** like `BankSignature.Verify`, a bad certificate yields a
> clean rejection with a structured reason; only `null` arguments throw. Callers who want
> throw semantics check `if (!result.IsValid) throw …` at their level.

## EBICS version relation

| Version | Key exchange | X.509 check |
|---|---|---|
| H003 / H004 | pure RSA keys (`RSAKeyValue`), trust via INI fingerprint [#22](public-key-fingerprint.md) | **not applicable** (`CertificateRequirement.NotUsed`) — the verifier is not called |
| H005 | certificate (`X509Data`) | **required** (`CertificateRequirement.Required`) — full chain/validity/usage check |

The "procedure without certificates" is thus modelled as policy: the onboarding layer queries
`CertificateRequirements.For(version)` and calls the verifier only in the `Required` case. The verifier
itself thus keeps a single responsibility (checking certificates) and never receives a certificate it
should not check.

## Tests

`tests/EBICO.Tests/Crypto/X509CertificateVerifierTests.cs` and the extended
`tests/EBICO.Tests/Infrastructure/TestCertificates(Tests).cs` (Tier A, CI-safe, in-process CA via
`TestCertificates.CreateCertificateAuthority`/`IssueCertificate`; deterministic via
`VerificationTime` + `NoCheck`):

- **Happy path:** leaf chains to a trusted test CA; self-signed in the trust store;
  Root→Intermediate→Leaf (intermediate in `ExtraStore`); correct KeyUsage per `KeyPurpose`;
  `VerificationTime` within the window; matching `ExpectedPublicKey`.
- **Negative cases (each a specific `Errors` bit):** untrusted root; expired (`Expired`);
  not yet valid (`NotYetValid`); wrong KeyUsage; missing KeyUsage extension; self-signed not
  trusted; `KeyMismatch`; non-RSA (ECDSA) → `NotRsa`; multiple errors (expired + untrusted).
- **Revocation:** `NoCheck` reports no `Revoked`/`RevocationStatusUnknown` (a real revoked test
  needs CRL/OCSP → integration-only, not a unit test).
- **Null args:** `Verify(null, …)` / `Verify(cert, null)` / `Verify(cert, null-anchors, …)` → throw.
- **Pure-key:** `CertificateRequirements.For` maps H003/H004→`NotUsed`, H005→`Required`, unknown→throw.

## Related

- [Key pairs & representation (A/E/X)](key-representation.md) — underlying key layer (#18)
- [Public-key fingerprints (HPB/INI/HIA)](public-key-fingerprint.md) — trust in the pure-key procedure (#22)
- [Bank-technical signature A005/A006](bank-signature.md) — shares the `Verify` convention (#19)
- [ADR-0008 — Crypto library](../adr/0008-krypto-bibliothek.md)
