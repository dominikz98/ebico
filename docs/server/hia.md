# Server: HIA — Sending the Auth & Enc keys (X002/E002)

> Implementation of **Issue #27** (Milestone M3 — Server: Key Management). This page
> describes the emulator's second business **order handler**: receiving a subscriber's
> public **authentication key** (X00x) and **encryption key**
> (E00x) via **HIA**, the server-side **storage** of both keys
> and the lifecycle transition **`Initialized → Ready`**.
>
> Deliberately **included**: OrderType-`HIA` processing for H003/H004/H005, extraction and
> storage of the X00x and E00x key, response as `ebicsKeyManagementResponse`,
> return codes for the error cases (INI not yet run, unknown subscriber,
> already completed, malformed order data).
> Deliberately **not yet**: HPB (#28), response signature (X002, M4), persistence of the
> key store (in-memory remains the default), certificate-chain checking for H005 (M8),
> the complete return code catalogue (#36/M4), free INI/HIA ordering (see spec caveats).

## Purpose

HIA is the second step of subscriber initialisation (after [INI](ini.md)): the
client sends an **unsecured** `ebicsUnsecuredRequest` whose order data carries the
self-describing `HIARequestOrderData` document with the public
authentication key (version X001/X002 — "X00x") and the
encryption key (version E001/E002 — "E00x"). The server accepts both
keys, stores them and marks the subscriber as `Ready`.

The client counterpart (key generation, sending HIA) is implemented in the connector
(see [Onboarding flows](../connector/onboarding.md)) and delivers exactly the order data
that this handler consumes.

## Flow

The pipeline (`EbicsRequestPipeline`) recognises the unsecured request, pulls the
OrderType `HIA` from the header and forwards it to the version-matching handler. The
version-agnostic flow lives in `HiaOrderHandlerBase`, the version-specific
key extraction in `H003`/`H004`/`H005HiaOrderHandler`:

| Step | Action |
| --- | --- |
| 1. Extraction | `Body/DataTransfer/OrderData` (base64 decoded by the binding) → `EbicsCompression.Decompress` → `EbicsXmlSerializer.Deserialize<HiaRequestOrderData>` |
| 2. Keys | Per key — H003/H004: `PubKeyValue/RSAKeyValue` (Modulus/Exponent) → `RsaKeyImportExport.ImportRsaKeyValue`. H005: `X509Data` → `RsaKeyImportExport.ImportPublicKeyFromCertificate` |
| 3. Version check | `AuthenticationVersion` must be an X00x version, `EncryptionVersion` an E00x version, and both permitted for the protocol version (`KeyVersions.EnsurePermitted`) |
| 4. Subscriber | `IMasterDataManager.GetSubscriberAsync` — must exist and be in state `Initialized` (INI run beforehand) |
| 5. Storage | both public keys → `IServerKeyStore.StoreAsync` (keyed on subscriber × `KeyPurpose.Authentication` respectively `KeyPurpose.Encryption`) |
| 6. State | `IMasterDataManager.TransitionSubscriberAsync(…, Ready)` |
| 7. Response | `ebicsKeyManagementResponse` with `000000`/`000000` (`EbicsResponseFactory.BuildKeyManagementResponse`) |

Example — HIA order data (H004, abridged), before compression/base64:

```xml
<HIARequestOrderData xmlns="urn:org:ebics:H004" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
  <AuthenticationPubKeyInfo>
    <PubKeyValue>
      <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    </PubKeyValue>
    <AuthenticationVersion>X002</AuthenticationVersion>
  </AuthenticationPubKeyInfo>
  <EncryptionPubKeyInfo>
    <PubKeyValue>
      <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    </PubKeyValue>
    <EncryptionVersion>E002</EncryptionVersion>
  </EncryptionPubKeyInfo>
  <PartnerID>PARTNER01</PartnerID>
  <UserID>USER01</UserID>
</HIARequestOrderData>
```

Success response (H004, abridged):

```xml
<ebicsKeyManagementResponse xmlns="urn:org:ebics:H004" Version="H004">
  <header authenticate="true">
    <static/>
    <mutable><ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body><ReturnCode>000000</ReturnCode></body>
</ebicsKeyManagementResponse>
```

## Key store

The server holds received public keys in the `IServerKeyStore`
(default `InMemoryServerKeyStore`, overridable via `TryAddSingleton`). It is keyed on
(`HostId`, `PartnerId`, `UserId`) × `KeyPurpose` and stores exclusively the
**public** key plus the EBICS key version (`StoredPublicKey`). HIA stores
two entries: the authentication key (`X00x`, `KeyPurpose.Authentication`)
and the encryption key (`E00x`, `KeyPurpose.Encryption`). They sit
purpose-isolated alongside the signature key (`A00x`) already stored via [INI](ini.md).
The domain aggregate `Subscriber` remains deliberately key-free (see
[Master data](master-data.md)).

## Return codes & error cases

As across the entire `/ebics` endpoint, protocol/business errors are answered with **HTTP 200** and
a return code in the envelope (see [host.md](host.md)); the business code
sits in `body/ReturnCode`.

| Situation | Return code |
| --- | --- |
| HIA accepted | `000000` EBICS_OK |
| Subscriber unknown, **not yet** `Initialized` (INI missing) **or** no longer `Initialized` (already `Ready`/`Suspended`) | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Order data cannot be decompressed/deserialised, unusable key material or wrong/impermissible auth/enc version | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

HIA is therefore only accepted in state `Initialized`; a repeated HIA (subscriber already
`Ready`) is **strictly rejected** — this matches the domain's permitted
transitions (`Initialized → Ready`).

### ⚠️ Spec caveats

- **INI-before-HIA ordering is enforced.** Since the domain model only knows
  `New → Initialized → Ready` (no intermediate state for "HIA done, INI missing"),
  HIA accepts only an `Initialized` subscriber and thereby presupposes INI. The
  EBICS specification in principle allows INI/HIA in any order; this
  simplification is to be verified against the official flow.
- **`Ready` without a separate activation step.** HIA switches directly to `Ready`. In
  practice a subscriber only becomes active after the INI/HIA letters are reconciled and
  explicitly activated by the bank; the emulator anticipates this step (lacking an
  operator/letter workflow) implicitly.
- The concrete codes (`091002` for state errors, `090004` for order-data format) are
  to be verified against the official EBICS Annex 1; the complete, central
  return code catalogue arrives with **#36 (M4)**.
- The response is **unsigned** — the response authentication signature (X002) is **M4**;
  strict clients might reject unsigned responses (consistent with `EbicsResponseFactory`).
- **H005:** only the public key is extracted from the transmitted certificates and
  stored; a certificate-chain/self-signature check is a conformance topic (**M8**).
- `OrderAttribute`/`SecurityMedium` are not enforced (unverified, as in the connector).

## EBICS version mapping

| Version | Order data | Key transport | OrderType field |
| --- | --- | --- | --- |
| H003 / H004 | `H00x.HIARequestOrderData` | `RSAKeyValue` (Modulus/Exponent) per key | `OrderType` |
| H005 | `H005.HIARequestOrderData` | `X509Data` (certificate) per key | `AdminOrderType` |

Permitted versions (via `KeyVersions`): authentication **X001** (H003/H004 only),
**X002** (all); encryption **E001** (H003/H004 only), **E002** (all). A version
impermissible for the protocol version (e.g. E001 on H005) or a purpose-alien version
(e.g. A005 as `AuthenticationVersion`) is rejected with `090004`.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; request XML from committed
Core bindings, no proprietary fixtures):

- `HiaOrderHandlerTests` — end-to-end via `EbicsRequestPipeline`, `[Theory]` over H003/H004/H005:
  happy path (response `ebicsKeyManagementResponse` `000000`, subscriber `Initialized→Ready`,
  **both** keys in the `IServerKeyStore` with matching modulus/version) plus negative cases:
  subscriber still `New` (INI missing), unknown subscriber and already `Ready` (`091002`),
  undecodable order data (`090004`), a version impermissible for the protocol version (E001/H005) or
  purpose-alien (A005 as AuthenticationVersion) (`090004`).
- `InMemoryServerKeyStoreTests` — Store/Get/Contains, purpose isolation, overwrite, subscriber isolation.

## Related documentation

- [INI — Sending the signature keys (A00x)](ini.md) — the preceding onboarding step
- [Hostable server skeleton](host.md) — host, pipeline, return codes, response factory
- [Master data management](master-data.md) — subscriber lifecycle, `IMasterDataManager`, store
- [Onboarding flows INI / HIA / HPB](../connector/onboarding.md) — the client counterpart
- [Key pairs & representation (A/E/X)](../protocol/key-representation.md) — key versions, RSAKeyValue/X.509 import
- [Public key fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md) — HIA letter reconciliation
