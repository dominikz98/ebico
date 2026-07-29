# Server: HPB — Retrieving the bank keys

> Implementation of **Issue #28** (Milestone M3 — Server: Key Management). This page
> describes the emulator's third business **order handler**: returning the
> public **bank keys** — authentication (`X00x`) and encryption (`E00x`)
> — to an already-initialised subscriber via **HPB**, as an **encrypted**,
> HPB-conformant response.
>
> Deliberately **included**: OrderType-`HPB` processing for H003/H004/H005, a server-side
> **bank key store** (`IServerBankKeyStore`, auto-generated & seedable), building of the
> `HPBResponseOrderData`, compression + **E002 encryption** of the order data for the
> subscriber, `EncryptionPubKeyDigest` for reconciliation, response as `ebicsKeyManagementResponse`
> with a populated `DataTransfer`, return codes for the error cases.
> Deliberately **not yet**: checking the **X002 request signature** and **signature of the response**
> (X002, M4), persistence of the key store (in-memory remains the default),
> certificate-chain checking for H005 (M8), the complete return code catalogue (#36/M4).

## Purpose

HPB is the **download counterpart** to [INI](ini.md) and [HIA](hia.md): after a
subscriber has uploaded its public keys (state `Ready`), it retrieves via
**HPB** the public keys **of the bank** — the authentication key
(`X002`) and the encryption key (`E002`) — in order to verify or decrypt future
server responses.

Unlike INI/HIA (unsecured uploads whose response is only a return code), HPB is
a **signed** `ebicsNoPubKeyDigestsRequest` whose response carries an **encrypted
payload body**: the `HPBResponseOrderData` with the bank keys, **compressed**
and **encrypted** with the subscriber's `E002` key (E002 hybrid: AES-128-CBC
for the data, RSA-OAEP for the transaction key). This way only the subscriber holding the
private E002 key can read the response.

The client counterpart (sending HPB, decrypting the response, fingerprint reconciliation) is
implemented in the connector (see [Onboarding flows](../connector/onboarding.md)).

## Flow

The pipeline (`EbicsRequestPipeline`) recognises the `ebicsNoPubKeyDigestsRequest`, pulls the
OrderType `HPB` from the header and forwards it to the version-matching handler. The
version-agnostic flow lives in `HpbOrderHandlerBase`, the version-specific build of the
bank-key order data in `H003`/`H004`/`H005HpbOrderHandler`:

| Step | Action |
| --- | --- |
| 1. Identification | read `Header/Static` (`HostID`/`PartnerID`/`UserID`) from the `ebicsNoPubKeyDigestsRequest` (`ExtractHpbRequest`) |
| 2. Subscriber | `IMasterDataManager.GetSubscriberAsync` — must exist and be in state `Ready` (INI **and** HIA run) |
| 3. Recipient key | subscriber's E002 public key from `IServerKeyStore.GetAsync(…, KeyPurpose.Encryption)` (stored at HIA) |
| 4. Bank keys | `IServerBankKeyStore.GetOrCreateAsync(hostId)` supplies the bank's own `X002`/`E002` pair |
| 5. Order data | build `HPBResponseOrderData` version-specifically (H003/H004: `PubKeyValue/RSAKeyValue`; H005: `X509Data`) → `EbicsXmlSerializer.SerializeOrderData` |
| 6. Encryption | `EbicsCompression.Compress` → `EncryptionE002.Encrypt(compressed, subscriberE002, version)` → `EncryptedOrderData` |
| 7. Digest | `PublicKeyFingerprint.Compute(subscriberE002)` for `DataEncryptionInfo/EncryptionPubKeyDigest` |
| 8. Response | `ebicsKeyManagementResponse` with `000000` and populated `Body/DataTransfer` (`EbicsResponseFactory.BuildKeyManagementResponse(version, payload)`) |

**No** state transition: HPB is read-only, the subscriber stays `Ready`.

Example — HPB order data (H004, abridged), before compression/encryption:

```xml
<HPBResponseOrderData xmlns="urn:org:ebics:H004" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
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
  <HostID>EBICOHOST</HostID>
</HPBResponseOrderData>
```

Success response (H004, abridged) — the order data is base64(E002-encrypt(zlib(orderData))):

```xml
<ebicsKeyManagementResponse xmlns="urn:org:ebics:H004" Version="H004">
  <header authenticate="true">
    <static/>
    <mutable><ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body>
    <DataTransfer>
      <DataEncryptionInfo authenticate="true">
        <EncryptionPubKeyDigest Version="E002"
          Algorithm="http://www.w3.org/2001/04/xmlenc#sha256">…</EncryptionPubKeyDigest>
        <TransactionKey>…</TransactionKey>
      </DataEncryptionInfo>
      <OrderData>…</OrderData>
    </DataTransfer>
    <ReturnCode>000000</ReturnCode>
  </body>
</ebicsKeyManagementResponse>
```

## Bank keys & key store

For HPB the server needs its **own** key pair. The new
`IServerBankKeyStore` holds it (default `InMemoryServerBankKeyStore`, overridable via
`TryAddSingleton`), keyed on `HostId` (multi-bank capable). `GetOrCreateAsync` **generates
the pair on demand** (`RsaKeyMaterial.Generate()`, versions `X002`/`E002`) and **caches it
process-wide** — so a repeated HPB returns the same keys (important for the client's
fingerprint reconciliation). `SetAsync` allows seeding a known pair
(tests, fixed emulator identities).

The `E002` public key of the **subscriber** needed to **encrypt** the response lives in the
`IServerKeyStore` (from [HIA](hia.md), `KeyPurpose.Encryption`). The domain aggregate `Bank`
remains deliberately key-free (see [Master data](master-data.md)); key material is a
matter of the server layer.

## Return codes & error cases

As across the entire `/ebics` endpoint, protocol/business errors are answered with **HTTP 200** and a
return code in the envelope (see [host.md](host.md)); the business code sits in
`body/ReturnCode`.

| Situation | Return code |
| --- | --- |
| HPB successful (encrypted bank keys) | `000000` EBICS_OK |
| Subscriber unknown **or** not `Ready` (INI/HIA not completed) **or** `Ready` without a stored E002 key | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Wrong request type (e.g. an `ebicsRequest` with OrderType `HPB` instead of `ebicsNoPubKeyDigestsRequest`) | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

### ⚠️ Spec caveats

- **The request signature (X002) is not checked.** The HPB request is X002-signed; checking
  the `AuthSignature` is **M4** (the verify stage stays No-Op as with INI/HIA). Confidentiality
  is nevertheless preserved because the response is encrypted with the **subscriber's E002 key**
  — only its private key can decrypt it.
- **The response is unsigned** — the response authentication signature (X002) is likewise
  **M4** (consistent with `EbicsResponseFactory`); strict clients might reject unsigned responses.
- **`Ready` is presupposed.** HPB requires a `Ready` subscriber (INI + HIA run).
  EBICS practice may allow HPB even before final activation; this simplification
  is to be verified against the official flow (cf. [hia.md](hia.md)).
- **Auto-generated bank keys.** The emulator creates the bank key pair on demand and
  holds it only in memory; via `IServerBankKeyStore.SetAsync` a fixed/persisted pair
  can be used.
- **H005:** the bank keys are delivered as freshly **self-signed** certificates; a
  certificate-chain check is a conformance topic (**M8**). Trust arises via the
  public key fingerprint, not the chain.
- **Only `E002` encryption keys are served.** The response is encrypted via the
  transport encryption `EncryptionE002` (RSA-OAEP) for the subscriber key.
  An uploaded **legacy `E001` key** (permitted at HIA on H003/H004, PKCS#1-v1.5) is
  not supported by the E002 building block; HPB then fails with `061099` (EBICS_INTERNAL_ERROR) instead of
  a business rejection. `E002` is the project-wide standard; a general encryption
  dispatch (E001/PKCS#1) is not part of #28.
- The concrete codes (`091002`, `090004`) are to be verified against the official EBICS Annex 1;
  the central return code catalogue arrives with **#36 (M4)**. The `E002`-OAEP-vs-PKCS1
  and IV/compression caveat lives in the crypto building blocks (`EncryptionE002`,
  `EbicsCompression`).

## EBICS version mapping

| Version | Order data | Key transport | OrderType field |
| --- | --- | --- | --- |
| H003 / H004 | `H00x.HPBResponseOrderData` | `RSAKeyValue` (Modulus/Exponent) per key | `OrderType` |
| H005 | `H005.HPBResponseOrderData` | `X509Data` (self-signed certificate) per key | `AdminOrderType` |

The bank key versions are `X002` (auth) and `E002` (enc) — the defaults permitted for all
supported protocol versions (`KeyVersions.Default`). The `EncryptionPubKeyDigest`
uses SHA-256 (`http://www.w3.org/2001/04/xmlenc#sha256`) over the subscriber's E002 key.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; request XML from committed
Core bindings, no proprietary fixtures):

- `HpbOrderHandlerTests` — end-to-end via `EbicsRequestPipeline`, `[Theory]` over H003/H004/H005:
  happy path (response `ebicsKeyManagementResponse` `000000`, response actually **decrypted** with the
  subscriber's private E002 key, bank keys = contents of the `IServerBankKeyStore`,
  `EncryptionPubKeyDigest` checked, subscriber stays `Ready`), stability over repeated calls,
  a full **INI → HIA → HPB** run (HPB decryptable with the key transmitted at HIA)
  plus negative cases: subscriber `New`/`Initialized`, unknown subscriber, `Ready` without an E002 key
  (`091002`).
- `InMemoryServerBankKeyStoreTests` — GetOrCreate caches/stable per host (X002/E002, with private
  key), differs per host, `SetAsync` overwrites/validates.

## Related documentation

- [INI — Sending the signature keys (A00x)](ini.md) — first onboarding step
- [HIA — Sending the Auth & Enc keys (X002/E002)](hia.md) — second onboarding step, supplies the E002 recipient key
- [Hostable server skeleton](host.md) — host, pipeline, return codes, response factory
- [Master data management](master-data.md) — subscriber lifecycle, `IMasterDataManager`, store
- [Onboarding flows INI / HIA / HPB](../connector/onboarding.md) — the client counterpart
- [Public key fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md) — fingerprint reconciliation of the bank keys
- [Transport encryption E002](../protocol/encryption-e002.md) — E002 hybrid (AES + RSA-OAEP)
