# Server: Key change & suspension — HCA / HCS / SPR / HSA

> Implementation of **Issue #29** (Milestone M3 — Server: Key Management). This page describes
> the four concluding key-management order handlers of the emulator and completes the series
> [INI](ini.md) → [HIA](hia.md) → [HPB](hpb.md):
>
> - **HCA** — key change: replaces **Auth** (`X00x`) + **Enc** (`E00x`) of an onboarded subscriber.
> - **HCS** — key change: replaces **all** keys (Sig `A00x` + Auth + Enc).
> - **SPR** — suspension/blocking: sets the subscriber to the state `Suspended`.
> - **HSA** — legacy initialisation of Auth + Enc (only **H003/H004**, dropped in H005).
>
> Deliberately **included**: OrderType processing of HCA/HCS/SPR (signed `ebicsRequest`) and
> HSA (`ebicsUnsecuredRequest`), **E002 decryption** of the HCA/HCS order data with the private
> bank key, key **replacement** via purpose upsert, consistent **state transitions**
> (SPR → `Suspended`), response as `ebicsResponse` (HCA/HCS/SPR) or `ebicsKeyManagementResponse`
> (HSA), return codes for the error cases.
> Deliberately **not yet**: checking of **order signature (ES)** and **X002 request signature** (M4),
> the generic upload **transaction machine** with segmentation (M4), a dedicated
> `Blocked` state (SPR uses `Suspended`), certificate-chain checking for H005 (M8), a complete
> return-code catalog (#36/M4).

## Purpose

After onboarding ([INI](ini.md) → [HIA](hia.md), state `Ready`) a subscriber must be able to **change**
its keys (routinely or on compromise) and, in the event of a fault, be able to be **blocked**:

- **HCA** (*change authentication/encryption*) swaps the authentication (`X00x`) and the
  encryption key (`E00x`). The bank-technical signature key remains.
- **HCS** (*change subscriber's keys*) swaps **all three** keys (signature `A00x` +
  Auth + Enc) — the combination of INI + HIA as a single key change.
- **SPR** (*suspension*) blocks the subscriber for order processing; it becomes `Suspended` and
  can only transact again after reactivation (`Suspended → Ready`, via the [admin API](master-data.md)).
  The keys are retained.
- **HSA** is the historic variant of HIA (only H003/H004): it transfers Auth + Enc in an
  `ebicsUnsecuredRequest` and is thus functionally identical to HIA.

Unlike INI/HIA (unsecured uploads), **HCA/HCS/SPR** are signed `ebicsRequest` uploads.
For HCA/HCS the order data is encrypted with the **public `E002` key of the bank** —
the opposite direction to [HPB](hpb.md): the server decrypts it with its **private**
`E002` key (`IServerBankKeyStore`).

## Flow

The pipeline (`EbicsRequestPipeline`) pulls the OrderType from the header (H003/H004: `OrderType`,
H005: `AdminOrderType`) and forwards to the version-matching handler. `ebicsRequest` orders
are answered with `ebicsResponse`, HSA (`ebicsUnsecuredRequest`) with
`ebicsKeyManagementResponse` — both without pipeline changes (see [host.md](host.md)).

### HCA / HCS — encrypted key change

Version-agnostic flow in `HcaOrderHandlerBase` / `HcsOrderHandlerBase`, the
version-specific key extraction in `H003`/`H004`/`H005{Hca,Hcs}OrderHandler`:

| Step | Action |
| --- | --- |
| 1. Envelope | Read `Header/Static` (IDs) + `Body/DataTransfer` (`DataEncryptionInfo/TransactionKey` + `OrderData`) from the `ebicsRequest` (`ExtractEnvelope`) |
| 2. Bank key | `IServerBankKeyStore.GetOrCreateAsync(hostId)` yields the bank pair **with private `E002` key** |
| 3. Decrypt | `EncryptionE002.Decrypt(TransactionKey + OrderData, bankE002)` → `EbicsCompression.Decompress` → order-data XML |
| 4. Extraction | `ParseOrderData` reads Auth+Enc (HCA) or Sig+Auth+Enc (HCS); H003/H004 `RSAKeyValue`, H005 `X509Data` |
| 5. Key policy | Purpose per key correct + `KeyVersions.EnsurePermitted(version, protocol)` |
| 6. Subscriber | `GetSubscriberAsync` — must exist and be `Ready` |
| 7. Replace | `IServerKeyStore.StoreAsync` per purpose (upsert **replaces** the old key) → `000000` |

**No** state transition: a key change leaves the subscriber `Ready`.

Example — HCA order data (H004, abridged), **before** compression/encryption:

```xml
<HCARequestOrderData xmlns="urn:org:ebics:H004" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
  <AuthenticationPubKeyInfo>
    <PubKeyValue><ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue></PubKeyValue>
    <AuthenticationVersion>X002</AuthenticationVersion>
  </AuthenticationPubKeyInfo>
  <EncryptionPubKeyInfo>
    <PubKeyValue><ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue></PubKeyValue>
    <EncryptionVersion>E002</EncryptionVersion>
  </EncryptionPubKeyInfo>
  <PartnerID>PARTNER01</PartnerID>
  <UserID>USER01</UserID>
</HCARequestOrderData>
```

HCS additionally adds a `SignaturePubKeyInfo` (H003/H004 in the `S001`, H005 in the `S002` namespace).
On the wire the order data is base64-encoded as `E002-encrypt(zlib(orderData))`:

```xml
<ebicsRequest xmlns="urn:org:ebics:H005" Version="H005">
  <header authenticate="true">
    <static><HostID>EBICOHOST</HostID><PartnerID>PARTNER01</PartnerID><UserID>USER01</UserID>
      <OrderDetails><AdminOrderType>HCA</AdminOrderType></OrderDetails></static>
    <mutable/>
  </header>
  <body>
    <DataTransfer>
      <DataEncryptionInfo authenticate="true">
        <EncryptionPubKeyDigest Version="E002" Algorithm="http://www.w3.org/2001/04/xmlenc#sha256">…</EncryptionPubKeyDigest>
        <TransactionKey>…</TransactionKey>
      </DataEncryptionInfo>
      <OrderData>…</OrderData>
    </DataTransfer>
  </body>
</ebicsRequest>
```

The success response is an `ebicsResponse` with `000000` in header **and** body.

### SPR — suspension

`SprOrderHandlerBase` (+ version derivations) reads only the header IDs — SPR carries **no**
order data (there is no `SPRRequestOrderData`):

| Step | Action |
| --- | --- |
| 1. Identification | Read `Header/Static` (IDs) from the `ebicsRequest` |
| 2. Subscriber | `GetSubscriberAsync` — must exist and must **not** already be `Suspended` |
| 3. Transition | `IMasterDataManager.TransitionSubscriberAsync(…, Suspended)` → `000000` |

The transition `New/Initialized/Ready → Suspended` is allowed in the state machine
(`Subscriber.IsAllowedTransition`). Any `DataTransfer`/order signature is ignored
(ES check is M4).

### HSA — legacy initialisation (H003/H004)

`HsaOrderHandlerBase` (+ H003/H004) mirrors [HIA](hia.md): store Auth + Enc from an
`ebicsUnsecuredRequest` and transition `Initialized → Ready`. The response is — as with INI/HIA —
an `ebicsKeyManagementResponse` with `000000`.

## Key store & state machine

- **Key replacement:** `IServerKeyStore` is an upsert per `(subscriber, KeyPurpose)`; storing
  a key of the same purpose **overwrites** the old one. HCA thus replaces Auth+Enc,
  HCS Sig+Auth+Enc; the respective other key remains untouched.
- **Bank key:** the HCA/HCS decryption uses the **private** `E002` key of the bank
  from `IServerBankKeyStore` (the same pair that [HPB](hpb.md) delivers and with which the client
  encrypted the order data).
- **State transitions** (`SubscriberState`, `Subscriber.IsAllowedTransition`) remain **unchanged**:
  `Suspended` and the edges `New/Initialized/Ready → Suspended` or `Suspended → Ready` already
  exist. HCA/HCS perform **no** transition (remain `Ready`); SPR goes to `Suspended`; HSA
  goes `Initialized → Ready`. SPR **removes no keys** — the suspension is reversible.

## Return codes & error cases

As across the entire `/ebics` endpoint, errors are answered with **HTTP 200** and a return code in the envelope
(see [host.md](host.md)); the functional code is in `body/ReturnCode`.

| Situation | Return code |
| --- | --- |
| HCA/HCS/SPR/HSA successful | `000000` EBICS_OK |
| Subscriber unknown or in the wrong state (HCA/HCS: not `Ready`; HSA: not `Initialized`; SPR: unknown or already `Suspended`) | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Order data not decryptable/unpackable/deserialisable, inadmissible/purpose-foreign key version, wrong request type | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

### ⚠️ Spec caveats

- **No signature check.** HCA/HCS/SPR are signed uploads (order signature/ES + X002); these
  signatures are **not** checked (the verify stage remains a no-op, **M4** — consistent with
  INI/HIA/HPB). The confidentiality of the HCA/HCS order data remains preserved, because it is encrypted for the
  bank `E002` key.
- **Simplified single-phase processing.** The signed upload is handled in one step;
  the generic transaction machine (initialisation/transfer, segmentation) is **M4**.
- **SPR → `Suspended`.** EBICS may distinguish temporary suspension from permanent blocking;
  EBICO maps SPR onto the existing `Suspended` state (no dedicated `Blocked` status). The
  reactivation runs out-of-band via the [admin API](master-data.md). An already `Suspended`
  subscriber cannot re-onboard via INI/HIA (no `Suspended → New/Initialized` edge).
- **HSA state assumption.** HSA requires `Initialized` (INI has run) and goes to `Ready` — analogous to
  HIA; the exact legacy flow is to be verified against the official annex.
- **H005:** keys are transported as `X509Data` certificates; only the public key is
  extracted, a certificate-chain check is **M8**.
- **Only `E002`.** The HCA/HCS decryption uses `EncryptionE002` (RSA-OAEP); a legacy `E001`
  is not supported. The concrete codes (`091002`/`090004`) are to be verified against EBICS Annex 1;
  the central return-code catalog comes with **#36 (M4)**.

## EBICS version mapping

| Order | Envelope | Order data | Key transport | Versions |
| --- | --- | --- | --- | --- |
| HCA | signed `ebicsRequest` (encrypted) | `HCARequestOrderData` (Auth + Enc) | H003/H004 `RSAKeyValue`, H005 `X509Data` | H003/H004/H005 |
| HCS | signed `ebicsRequest` (encrypted) | `HCSRequestOrderData` (Sig + Auth + Enc) | H003/H004 `RSAKeyValue` (Sig: `S001`), H005 `X509Data` (Sig: `S002`) | H003/H004/H005 |
| SPR | signed `ebicsRequest` | — (none) | — | H003/H004/H005 |
| HSA | `ebicsUnsecuredRequest` | `HSARequestOrderData` (Auth + Enc) | `RSAKeyValue` | **only H003/H004** |

OrderType field: H003/H004 `OrderType`, H005 `AdminOrderType`. Response type: `ebicsResponse` for
HCA/HCS/SPR, `ebicsKeyManagementResponse` for HSA.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; request XML from committed
Core bindings, no proprietary fixtures), end-to-end via `EbicsRequestPipeline`:

- `HcaOrderHandlerTests` — `[Theory]` over H003/H004/H005: happy path (response `ebicsResponse`
  `000000`, Auth+Enc **replaced** in the `IServerKeyStore` — new modulus ≠ old, subscriber stays
  `Ready`) plus negative cases (subscriber not `Ready`, unknown → `091002`; unpackable
  order data, purpose-foreign key version → `090004`).
- `HcsOrderHandlerTests` — like HCA, additionally the **signature key** replaced.
- `SprOrderHandlerTests` — suspension from `New`/`Initialized`/`Ready` (→ `Suspended`, response
  `ebicsResponse` `000000`); negative cases (unknown, already `Suspended` → `091002`).
- `HsaOrderHandlerTests` — `[Theory]` over H003/H004: happy path (`Initialized → Ready`, Auth+Enc
  stored); negative cases (subscriber `New`/`Ready`/unknown → `091002`; corrupt order data,
  purpose-foreign version → `090004`).
- `EbicoServerServiceCollectionExtensionsTests` — the DI registration wires HCA/HCS/SPR per version
  and HSA for H003/H004.

The request builders (among others `BuildEncryptedHcaRequest`/`BuildEncryptedHcsRequest` with
`EncryptionE002.Encrypt` against the bank public key, `BuildSprRequest`, `BuildUnsecuredHsaRequest`)
live in `ServerTestHelpers`.

## Related documentation

- [INI — sending the signature keys (A00x)](ini.md) — first onboarding step
- [HIA — sending the Auth & Enc keys (X002/E002)](hia.md) — HSA is the legacy variant of this
- [HPB — retrieval of the bank keys](hpb.md) — opposite direction of the E002 encryption
- [Hostable server skeleton](host.md) — host, pipeline, return codes, response factory
- [Master data management](master-data.md) — subscriber lifecycle, state machine, admin API
- [Transport encryption E002](../protocol/encryption-e002.md) — E002 hybrid (AES + RSA-OAEP)
