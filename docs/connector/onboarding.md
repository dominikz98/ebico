# Connector: Onboarding flows INI / HIA / HPB

> Implementation of **issue #47** (milestone M6 — Connector). This page describes the
> client-side onboarding flows of the `EBICO.Connector`: the key generation, the sending of
> INI and HIA, the fetching and verification of the bank keys via HPB as well as the INI/HIA letter.
> The basis is the [client core](client-core.md) (#46); the overall design is in the
> [Connector architecture](architecture.md).

## Purpose

Before a subscriber can send business orders (upload/download), the key exchange must be
complete:

1. **Key generation** — the subscriber generates three RSA pairs: signature (`A00x`),
   authentication (`X002`) and encryption (`E002`).
2. **INI** — sends the public **signature key** (unsecured).
3. **HIA** — sends the public **authentication and encryption keys** (unsecured).
4. **INI/HIA letter** — is printed, signed and transmitted (by post/fax) to the bank.
   The bank compares the key hashes printed in it with the ones received via INI/HIA.
5. **HPB** — the subscriber fetches the public **bank keys** (X002/E002), verifies them
   against the bank letter (hash comparison) and stores them.

```mermaid
sequenceDiagram
    participant C as Subscriber (connector)
    participant S as EBICS server (bank)
    C->>S: INI — public A00x signature key (unsecured)
    S-->>C: return code
    C->>S: HIA — public X002/E002 keys (unsecured)
    S-->>C: return code
    Note over C,S: INI/HIA letter with key hashes sent to the bank manually;<br/>the bank activates the subscriber.
    C->>S: HPB — fetch the bank keys (X002-signed)
    S-->>C: bank's X002/E002 (E002-encrypted)
    Note over C: verify bank hashes against the bank letter, then store in the IKeyStore.
```

## Public API

All requests follow the mediator pattern (`IEbicsRequest<TResult>`; see [client core](client-core.md)):

```csharp
services.AddEbicoConnector(o => { /* Url, HostId, PartnerId, UserId, Version */ })
        .Services.AddEbicoOnboarding();

// 1. Generate the keys (once, outside the send pipeline).
var keys = await keyGenerator.GenerateAsync();          // ISubscriberKeyGenerator

// 2./3. Send INI + HIA.
var ini = await client.Send(new IniRequest());          // -> IniResult (with letter)
var hia = await client.Send(new HiaRequest());          // -> HiaResult (with letter)

// 4. Write out the letter (text + PDF).
File.WriteAllText("ini-letter.txt", ini.Value!.Letter!.Text);
File.WriteAllBytes("ini-letter.pdf", ini.Value!.Letter!.Pdf!);

// 5. Fetch the bank keys + verify them against the bank letter.
var hpb = await client.Send(new HpbRequest
{
    ExpectedAuthenticationKeyDigest = bankLetterAuthDigest,
    ExpectedEncryptionKeyDigest     = bankLetterEncDigest,
});
```

| Type | Role |
| --- | --- |
| `ISubscriberKeyGenerator` | Generates A00x/E002/X002, stores them in the `IKeyStore` (`KeyOwner.Subscriber`). **Explicit**, one-time, outside of `Send`. |
| `IniRequest` / `IniResult` | Send INI; the result carries version, fingerprint (wire + letter format) and the letter. |
| `HiaRequest` / `HiaResult` | Send HIA; analogous, for auth and enc keys. |
| `HpbRequest` / `HpbResult` | Fetch bank keys; optional expected fingerprints + trust anchor (H005). The result carries the `BankKeys`. |
| `InitializationLetter` | `{ Text, byte[]? Pdf }` — result of the letter generation. |
| `EbicsOnboardingException` | Security/integrity error (hash mismatch, certificate error, malformed response). |

## Version dispatch (H003/H004/H005)

The envelope and PubKeyInfo types are **their own CLR classes per version** and differ
on three axes:

| Axis | H003 / H004 (pure keys) | H005 (certificate-based) |
| --- | --- | --- |
| PubKey representation | `PubKeyValue`/`RSAKeyValue` (modulus/exponent) | only `X509Data` (certificate) |
| Order details | `OrderType` + `OrderAttribute` | `AdminOrderType` |
| INI OrderData namespace | `S001` | `S002` |

This is encapsulated in **one `IOnboardingEnvelopeBuilder` per version** behind an
`IOnboardingEnvelopeBuilderRegistry` (same pattern as `EbicsVersions`/`KeyVersions`). The builder
builds the requests **and** parses the version-specific responses, so that the three handlers stay
version-agnostic (`IEbicsRequestEnvelope`, `KeyManagementResponseView`, `BankKeys`). H003/H004 share
the base `OnboardingEnvelopeBuilderBase`; H005 uses X.509. For H005 the handler generates a
short-lived self-signed certificate per key (`SelfSignedCertificateFactory`).

## Process per flow (handlers)

- **INI/HIA** (`IniRequestHandler`, `HiaRequestHandler`): fetch keys from `IKeyStore` →
  build OrderData (`SignaturePubKeyOrderData` or `HIARequestOrderData`) → **compress**
  (`EbicsCompression`, ZIP/zlib) → base64 → `ebicsUnsecuredRequest` → serialize → transport →
  parse response → return code → `EbicsResult` (+ letter).
- **HPB** (`HpbRequestHandler`): build `ebicsNoPubKeyDigestsRequest` → serialize →
  set the **X002 authentication signature** (`AuthenticationSignature.Sign`) → transport → return
  code → **E002 decrypt** (`EncryptionE002.Decrypt`, private subscriber E002 key) →
  **decompress** → parse `HPBResponseOrderData` → **fingerprint comparison**
  (`PublicKeyFingerprint.Verify`) against the bank letter → optional X.509 chain check
  (`X509CertificateVerifier`, H005) → store bank keys in the `IKeyStore` (`KeyOwner.Bank`).

**Error boundary:** business return codes → `EbicsResult.Failure` (no throw); technical or
security errors (hash mismatch, certificate error) → `EbicsOnboardingException`. On a mismatch the
bank keys are **not** stored.

## Reused Core building blocks

`EbicsXmlSerializer` (serialization, new: `SerializeOrderData`), `EbicsCompression` (new),
`PublicKeyFingerprint` (Compute/Verify/ToLetterFormat), `AuthenticationSignature` (X002),
`EncryptionE002` (hybrid), `RsaKeyMaterial` (new: `Generate`), `RsaKeyImportExport`,
`SelfSignedCertificateFactory` (new) + `EbicsCertificateProfile` (new, shared with the
`X509CertificateVerifier`), `KeyVersions`, `CertificateRequirements`.

## INI/HIA letter (text + PDF)

`IInitializationLetterRenderer` generates the letter from a pure `InitializationLetterModel`
(date injected via `TimeProvider`, therefore deterministic). The `TextInitializationLetterRenderer`
is dependency-free; the `PdfInitializationLetterRenderer` registered via `AddEbicoOnboarding()`
additionally produces a PDF via **QuestPDF** (community license, [ADR-0010](../adr/0010-pdf-bibliothek.md)).
The fingerprint appears in the letter in the grouped hex representation of
`PublicKeyFingerprint.ToLetterFormat` (8 bytes per line).

## Spec caveats

Encapsulated in seams and to be verified against the official EBICS annexes:
compression method (zlib vs. raw DEFLATE, `EbicsCompression`); the mapping `S001`↔H003/H004 or
`S002`↔H005 of the INI OrderData; whether H005 INI takes `A005` or `A006` as the default;
`OrderAttribute` (`DZNNN`/`DZHNN`) and `SecurityMedium` (`0000`) for H003/H004; return code source
(`Body/ReturnCode` primary). The X.509 KeyUsage profiles are centralized in `EbicsCertificateProfile`.

## Tests

`tests/EBICO.Tests/` — tier-A (self-constructed graphs, no proprietary samples):
key generation, `EbicsCompression` round-trip, `SelfSignedCertificateFactory` (KeyUsage +
verifier), letter (text assertions + PDF smoke), INI/HIA handler **per version** (round-trip: build
request → decompress/parse OrderData → embedded key = store key; OK/error return code), HPB handler
(decryption + storage; **hash mismatch → exception, nothing stored**; error return code →
`Failure`). The `FakeTransport` provides the simulated bank responses.

Since **#57** INI/HIA/HPB additionally run as a real round-trip against the in-process hosted
`EBICO.Server` — including the real state machine (`New → Initialized → Ready`) and bank key
fetching: [E2E: Connector ↔ Server](../development/e2e-connector-server.md).
