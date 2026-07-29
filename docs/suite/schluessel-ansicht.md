# Suite: Key/Certificate View

> Implementation of **Issue #55** (Milestone M7 — Suite). Builds on the UI shell
> ([#52](ui-shell.md)) and the crypto building blocks from M2: public-key fingerprints
> ([#22](../protocol/public-key-fingerprint.md)), key representation
> ([#18](../protocol/key-representation.md)) and certificate verification
> ([#23](../protocol/certificate-verification-x509.md)).

## Purpose

The `/schluessel` page is the key/certificate view of the inspector UI. It makes the
public keys of the bank and subscribers visible along with their SHA-256 fingerprints and
provides two tools: the **INI-letter comparison** (the manual fingerprint check that
a bank performs when an INI arrives) and **test-CA/key tools** for generating
RSA test keys and self-signed test certificates.

All crypto operations run through the existing primitives in `EBICO.Core.Crypto`; the
Suite references only `EBICO.Core`.

## Render mode

The page itself is **Static SSR** (pure display, loads the keys in `OnInitializedAsync`).
The two tools are **interactive islands**: the render mode is set at the embedding site
(`<IniLetterComparisonTool @rendermode="InteractiveServer" />`), not in the components themselves —
per [ADR-0009](../adr/0009-blazor-render-mode.md) ("interactivity per component"). They are the
first interactive components of the Suite.

## Structure

| Section | Content | Data source |
| --- | --- | --- |
| Known keys & fingerprints | Table of owner / purpose (A/E/X) / version / fingerprint (SHA-256, INI-letter format) | `IEmulatorStateProvider.GetKeysAsync` |
| Key versions (reference) | Catalogue of A004/A005/A006, E001/E002, X001/X002 with legacy flag, padding and permitted EBICS versions | `EBICO.Core.Crypto.KeyVersions` |
| INI-letter comparison | interactive tool (island) | `PublicKeyFingerprint.Verify` |
| Test CA & key tools | interactive tool (island) | `RsaKeyMaterial.Generate`, `SelfSignedCertificateFactory`, `X509CertificateVerifier` |

Data binding as with dashboard/master data via the read model `IEmulatorStateProvider`, here extended by
`GetKeysAsync()`. The keys come from the server-side key stores: the
subscriber keys (A/E/X) from `IServerKeyStore` (as INI/HIA store them during onboarding) and the
bank key pair (X/E) from `IServerBankKeyStore` (as HPB returns it) — bound in-process per
[ADR-0009](../adr/0009-blazor-render-mode.md). Since the Suite does not run an EBICS pipeline, the
`KeyStoreSeeder` fills these stores at startup with deterministic sample material (`KeyStoreSeedData`,
firmly embedded 2048-bit public keys); the fingerprints are precomputed by `KeyViewFactory` via
`PublicKeyFingerprint.Compute`. Bank keys are only read for the seeded hosts, so that
rendering the page does not create a fresh (non-reproducible) bank key pair.

The new DTO:

```csharp
public sealed record KeyView
{
    public required string OwnerLabel { get; init; }      // "Teilnehmer PARTNER01 / USER0001"
    public required KeyPurpose Purpose { get; init; }      // Signature / Encryption / Authentication
    public required string KeyVersion { get; init; }       // "A006"
    public required RsaKeyMaterial PublicKey { get; init; }
    public required string FingerprintText { get; init; }  // ToLetterFormat(Compute(PublicKey))
}
```

## Fingerprints & INI-letter comparison

The fingerprint is rendered for display with `PublicKeyFingerprint.ToLetterFormat` as grouped
uppercase hex (eight bytes per line) — exactly the representation of the INI letter.

The INI-letter comparison selects a known key (or takes a pasted public key
in PEM format via `RsaKeyImportExport.ImportFromPem`), reads the
fingerprint copied from the letter (hex, spaces/line breaks allowed — parsed by `FingerprintFormat.TryParseHex`)
and checks it **in constant time** with `PublicKeyFingerprint.Verify(key, expectedDigest)`. Result:
match, mismatch (with display of the actual fingerprint) or a friendly
error message on invalid input — never an exception.

## Test CA & key tools

- **Generate key:** `RsaKeyMaterial.Generate()` (2048 bit) → shows fingerprint and
  public-key PEM (`RsaKeyImportExport.ExportPublicKeyPem`).
- **Generate test certificate:** `SelfSignedCertificateFactory.Create(key, purpose, subject, …)` →
  verification with `X509CertificateVerifier.Verify(cert, { cert }, purpose)` (default
  `CustomRootTrust` + `NoCheck`, i.e. the self-signed certificate acts as its own trust anchor)
  → shows verdict, subject, validity and thumbprint.
- **Download:** public-key, private-key (`ExportPkcs8Pem`) and certificate PEM
  (`ExportCertificatePem`) are downloaded as a file via JS interop (`wwwroot/download.js`, function `ebicoDownload`).

> **⚠️ For testing only:** the generated keys/certificates are test material for the
> onboarding flows, not production key material.

## EBICS version reference

| Purpose | Key versions | Certificates |
| --- | --- | --- |
| Signature (A) | A004 (legacy, H003/H004), A005 (all), A006 (H005 only) | H005: `X509Data` instead of `RSAKeyValue` |
| Encryption (E) | E001 (legacy, H003/H004), E002 (all) | — |
| Authentication (X) | X001 (legacy, H003/H004), X002 (all) | — |

The fingerprint is version-agnostic (it only sees `RsaKeyMaterial`); the certificate tools
target H005 (EBICS 3.0), where keys are exchanged as certificates.

## Tests

`tests/EBICO.Tests/Suite/` covers:

- `SampleEmulatorStateProviderTests` — `GetKeysAsync` returns the sample keys; the
  fingerprint texts match the core computation; stable across calls.
- `KeyStoreSeederTests` — the `KeyStoreSeeder` stores the subscriber keys (A006/E002/X002) in the
  `IServerKeyStore` and the bank key pair (X002/E002, public-only) in the `IServerBankKeyStore` and is
  idempotent.
- `EmulatorStateProviderTests` — `GetKeysAsync` reads exactly the seeded keys from the stores
  (five entries, expected owners/versions, fingerprint == core computation); a subscriber without
  stored keys or a non-seeded bank produces no entry.
- `FingerprintFormatTests` — parsing hex with whitespace/case, round-trip against
  `ToLetterFormat`, rejection of invalid input.
- `SchluesselPageTests` (bUnit) — the page renders the key fingerprints and the
  KeyVersions catalogue.
- `IniLetterComparisonToolTests` (bUnit) — matching fingerprint → success, mismatching → error,
  invalid → warning.
- `TestKeyToolTests` (bUnit) — generate key/certificate, valid verdict, download via
  JS interop.

## Related

- [UI shell & navigation](ui-shell.md)
- [Public-key fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md)
- [Key pairs & representation (A/E/X)](../protocol/key-representation.md)
- [Certificate verification (X.509)](../protocol/certificate-verification-x509.md)
