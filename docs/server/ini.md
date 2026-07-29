# Server: INI — Sending the signature keys (A00x)

> Implementation of **Issue #26** (Milestone M3 — Server: Key Management). This page
> describes the emulator's first business **order handler**: receiving a subscriber's
> public bank-technical **signature key** (A00x) via **INI**, the server-side **storage**
> of the key, and the lifecycle transition **`New → Initialized`**.
>
> Deliberately **included**: OrderType-`INI` processing for H003/H004/H005, extraction and
> storage of the A00x key, response as `ebicsKeyManagementResponse`, return codes
> for the error cases (already initialised, unknown subscriber, malformed order data).
> Deliberately **not yet**: HIA/HPB (#27/#28), response signature (X002, M4), persistence of the
> key store (in-memory remains the default), certificate-chain checking for H005 (M8),
> the complete return code catalogue (#36/M4).

## Purpose

INI is the first step of subscriber initialisation: the client sends an
**unsecured** `ebicsUnsecuredRequest` whose order data carries the self-describing
`SignaturePubKeyOrderData` document with the public signature key (version
A004/A005/A006 — "A00x"). The server accepts the key, stores it,
and marks the subscriber as `Initialized`. The skeleton (#25, see
[host.md](host.md)) had prepared the pipeline extension points for this; #26
fills the first of them.

The client counterpart (key generation, sending INI) is implemented in the connector
(see [Onboarding flows](../connector/onboarding.md)) and delivers exactly the order data
that this handler consumes.

## Flow

The pipeline (`EbicsRequestPipeline`) recognises the unsecured request, pulls the
OrderType `INI` from the header and forwards it to the version-matching handler. The
version-agnostic flow lives in `IniOrderHandlerBase`, the version-specific
key extraction in `H003`/`H004`/`H005IniOrderHandler`:

| Step | Action |
| --- | --- |
| 1. Extraction | `Body/DataTransfer/OrderData` (base64 decoded by the binding) → `EbicsCompression.Decompress` → `EbicsXmlSerializer.Deserialize<SignaturePubKeyOrderData>` |
| 2. Key | H003/H004: `PubKeyValue/RSAKeyValue` (Modulus/Exponent) → `RsaKeyImportExport.ImportRsaKeyValue`. H005: `X509Data` → `RsaKeyImportExport.ImportPublicKeyFromCertificate` |
| 3. Version check | `SignatureVersion` must be an A00x version and permitted for the protocol version (`KeyVersions.EnsurePermitted`) |
| 4. Subscriber | `IMasterDataManager.GetSubscriberAsync` — must exist and be in state `New` |
| 5. Storage | public key → `IServerKeyStore.StoreAsync` (keyed on subscriber × `KeyPurpose.Signature`) |
| 6. State | `IMasterDataManager.TransitionSubscriberAsync(…, Initialized)` |
| 7. Response | `ebicsKeyManagementResponse` with `000000`/`000000` (`EbicsResponseFactory.BuildKeyManagementResponse`) |

Example — INI order data (H004, `S001`, abridged), before compression/base64:

```xml
<SignaturePubKeyOrderData xmlns="http://www.ebics.org/S001" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
  <SignaturePubKeyInfo>
    <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    <SignatureVersion>A005</SignatureVersion>
  </SignaturePubKeyInfo>
  <PartnerID>PARTNER01</PartnerID>
  <UserID>USER01</UserID>
</SignaturePubKeyOrderData>
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

The server holds received public keys in the new `IServerKeyStore`
(default `InMemoryServerKeyStore`, overridable via `TryAddSingleton`). It is keyed on
(`HostId`, `PartnerId`, `UserId`) × `KeyPurpose` and stores exclusively the
**public** key plus the EBICS key version (`StoredPublicKey`). INI stores
the signature key (`A00x`); HIA (#27) uses the same store for authentication
(`X00x`) and encryption keys (`E00x`). The domain aggregate `Subscriber` remains
deliberately key-free (see [Master data](master-data.md)).

## Return codes & error cases

As across the entire `/ebics` endpoint, protocol/business errors are answered with **HTTP 200** and
a return code in the envelope (see [host.md](host.md)); the business code
sits in `body/ReturnCode`.

| Situation | Return code |
| --- | --- |
| INI accepted | `000000` EBICS_OK |
| Subscriber unknown **or** no longer `New` (already initialised) | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Order data cannot be decompressed/deserialised, unusable/impermissible key material or wrong signature version | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

Re-INI is therefore **strictly rejected** as soon as the subscriber is no longer `New` — this
matches the domain's permitted transitions (`New → Initialized`).

### ⚠️ Spec caveats

- The concrete codes (`091002` for "already initialised", `090004` for order-data format)
  are to be verified against the official EBICS Annex 1; the complete, central
  return code catalogue arrives with **#36 (M4)**.
- The response is **unsigned** — the response authentication signature (X002) is **M4**;
  strict clients might reject unsigned responses (consistent with `EbicsResponseFactory`).
- **H005:** only the public key is extracted from the transmitted certificate and
  stored; a certificate-chain/self-signature check is a conformance topic (**M8**).
- `OrderAttribute`/`SecurityMedium` are not enforced (unverified, as in the connector).

## EBICS version mapping

| Version | Order data | Key transport | OrderType field |
| --- | --- | --- | --- |
| H003 / H004 | `S001.SignaturePubKeyOrderData` | `RSAKeyValue` (Modulus/Exponent) | `OrderType` |
| H005 | `S002.SignaturePubKeyOrderData` | `X509Data` (certificate) | `AdminOrderType` |

Permitted signature versions (via `KeyVersions`): **A004** (H003/H004 only), **A005** (all),
**A006** (H005 only). A version impermissible for the protocol version (e.g. A006 on H004)
is rejected with `090004`.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; request XML from committed
Core bindings, no proprietary fixtures):

- `IniOrderHandlerTests` — end-to-end via `EbicsRequestPipeline`, `[Theory]` over H003/H004/H005:
  happy path (response `ebicsKeyManagementResponse` `000000`, subscriber `New→Initialized`,
  key in the `IServerKeyStore` with matching modulus/version) plus negative cases: already
  initialised and unknown subscriber (`091002`), undecodable order data (`090004`),
  a version impermissible for the protocol version (A006/H004) or purpose-alien (X002) signature version (`090004`).
- `InMemoryServerKeyStoreTests` — Store/Get/Contains, purpose isolation, overwrite, subscriber isolation.

## Related documentation

- [Hostable server skeleton](host.md) — host, pipeline, return codes, response factory
- [Master data management](master-data.md) — subscriber lifecycle, `IMasterDataManager`, store
- [Onboarding flows INI / HIA / HPB](../connector/onboarding.md) — the client counterpart
- [Key pairs & representation (A/E/X)](../protocol/key-representation.md) — key versions, RSAKeyValue/X.509 import
- [Public key fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md) — INI letter reconciliation
