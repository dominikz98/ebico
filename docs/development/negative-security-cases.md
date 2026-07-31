# E2E: negative & security cases

> Implementation of **issue #58** (Milestone M8 — Validation & Conformance). Builds on the
> E2E harness from [#57](e2e-connector-server.md) and extends it by two things: the
> **productive check of the X002 authentication signature** in the server and a
> **negative suite** that tampers with a real, signed request on the wire.
>
> Deliberately **included**: server-side X002 verification of every signed `ebicsRequest`;
> E2E evidence that a tampered signature, a tampered (signed) header and
> tampered order data are rejected — each in **H003/H004/H005**.
>
> Deliberately **not yet** (documented spec caveats, see below): server-side check of
> **expired keys**, verification of the **ES/A00x** order signature, signature of the
> server **responses**.

## Purpose

Until #57 the connector did produce an X002 signature per `ebicsRequest`, but the server did not
check it (`NoOpEbicsRequestVerifier`). Thus the authentication signature was tested in **no**
direction. #58 switches the check on productively and evidences it end-to-end: only thereby
are "wrong signature" and "tampered order data" checkable at all as real rejections —
not just as self-consistent crypto primitives.

The load-bearing observation is **what** the X002 signature protects:

- The entire `EbicsRequestHeader` is `authenticate="true"` (likewise `DataEncryptionInfo`,
  `SignatureData`, `TransferReceipt`). Any tampering with the header — including `NumSegments`,
  `TransactionID`, `SegmentNumber` — breaks the reference digest and is rejected with **`061001`**
  `EBICS_AUTHENTICATION_FAILED`.
- The `OrderData` itself is **not** authenticated (it is E002-encrypted). Tampering
  with the ciphertext survives the signature check and fails only at decryption/decompression
  with **`090004`** `EBICS_INVALID_ORDER_DATA_FORMAT`.

## Server-side X002 verification

`src/EBICO.Server/Pipeline/X002EbicsRequestVerifier.cs` replaces the `NoOpEbicsRequestVerifier` as the
default (`AddEbicoServer`; still swappable via `TryAddSingleton`). The pipeline calls the
verifier for **every** request (`EbicsRequestPipeline`, stage *Verify*), so the selection lies
in the verifier:

1. **Only signed `ebicsRequest`** are checked (upload init/transfer, download init/transfer/
   receipt, HCA/HCS/SPR). `ebicsUnsecuredRequest` (INI/HIA/HSA) and `ebicsNoPubKeyDigestsRequest`
   (HPB) are skipped — they only initiate the key exchange or bootstrap it.
2. **Subscriber resolution:** init/single-phase requests carry the triple (HostID/PartnerID/UserID) in the
   static header; transfer/receipt requests carry only the HostID, the subscriber is bound to the
   transaction and is resolved via the upload/download transaction store.
3. **Verification runs only when an auth key is present** (after HIA). Before that there is nothing to check;
   a premature order is rejected by the state machine anyway (`091002`). If a key
   is stored, a valid `AuthSignature` is **mandatory** — absence or failure →
   `EbicsVerificationResult.Fail(EbicsReturnCode.AuthenticationFailed)` (`061001`, technically → header).

Verification is done via the existing crypto primitive
[`AuthenticationSignature.Verify`](../protocol/auth-signature-x002.md) against the subscriber's X002 key
stored in the `IServerKeyStore`.

## Covered cases

The negative suite (`tests/EBICO.Tests/E2E/NegativeSecurityE2ETests.cs`) runs a real CCT upload
and tampers with the already-signed request via a `RequestTamperingHandler` (sits above
the transport handler, armed **after** onboarding so that INI/HIA/HPB stay untouched).

| Case | H003 | H004 | H005 | Expectation |
| --- | :---: | :---: | :---: | --- |
| Tampered `SignatureValue` (init) | ✅ | ✅ | ✅ | `061001` `EBICS_AUTHENTICATION_FAILED` |
| Tampered header `NumSegments` (init) | ✅ | ✅ | ✅ | `061001` — X002 protects the segment metadata |
| Tampered `OrderData` (transfer) | ✅ | ✅ | ✅ | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` |

3 theories × 3 versions = **9 round-trips**. The happy-path evidence (that a **correct**
connector signature verifies server-side) is in the unchanged #57 suites
(`OnboardingE2ETests`/`UploadE2ETests`/`DownloadE2ETests`) — they stay green and thereby close
the sign→verify roundtrip across the boundary connector serialization ↔ server C14N.

## Return codes & error cases

| Situation | Return code |
| --- | --- |
| Tampered signature / tampered authenticated header | `061001` `EBICS_AUTHENTICATION_FAILED` |
| Tampered (non-authenticated) order-data ciphertext | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` |

The classic segment-inconsistency return codes require a **validly signed, but logically
inconsistent** request — which the connector never produces (and which X002 catches on the wire as `061001`).
They are therefore covered at the **server pipeline level** (hand-built, unsigned XML
against a verify-skipped subscriber):

| Situation | Return code | Test |
| --- | --- | --- |
| Duplicate segment (replay) | `091103` `EBICS_TX_MESSAGE_REPLAY` | `Server/UploadTransactionTests` |
| `lastSegment` before completeness (underrun) | `011101` `EBICS_TX_SEGMENT_NUMBER_UNDERRUN` | `Server/UploadTransactionTests` |
| Segment number > `NumSegments` | `091104` `EBICS_TX_SEGMENT_NUMBER_EXCEEDED` | `Server/UploadTransactionTests` |
| Unknown/expired transaction ID | `091101` `EBICS_TX_UNKNOWN_TXID` | `Server/{Upload,Download}TransactionTests` |
| Undecryptable/undecompressable order data | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` | `Server/UploadTransactionTests`, `Server/Hca*Tests` |

### ⚠️ Spec caveats

- **Expired keys are not checked server-side.** For subscriber RSA keys the
  server stores no validity window (`StoredPublicKey` = modulus/exponent + KeyVersion). The primitive
  `X509CertificateVerifier.RefineValidity` (expiry/not-yet-valid detection) exists, but is only used
  **client-side** (HPB bank certificate). Server-side expiry checking is key-management scope
  (M3/M4) and deliberately not part of #58.
- **ES/A00x order signature stays unchecked.** The *authorising* bank-technical signature of the
  order data is still only carried along, not verified.
- **Server responses are unsigned.** #58 checks the request direction; the signature of the
  `ebicsResponse` remains an open caveat (M4/M6).
- **C14N caveat persists:** the byte-exact canonicalization/reference detail of X002 is still
  not verified against the official annexes (see [X002 documentation](../protocol/auth-signature-x002.md)).
  The connector↔server roundtrip is consistent in itself (the happy-path E2E tests evidence it), the
  interop against real clients is the subject of
  [#59](conformance-real-clients.md).

## EBICS version reference

The procedure is identical across H003/H004/H005: what is checked is the C14N of the `authenticate="true"` nodes
(above all the `header`) plus the RSA signature over `SignedInfo`. The versions differ only in
the submission convention of the order (H003/H004 `OrderType`, H005 `AdminOrderType`+BTF) — irrelevant
for the signature check, since the entire header is signed. One authorisation set and one
auth key per subscriber cover all three versions.

| Aspect | H003 / H004 | H005 |
| --- | --- | --- |
| Authenticated area | `header` (`authenticate="true"`) + crypto metadata | identical |
| Auth key version | `X002` | `X002` |
| Not authenticated | `body/DataTransfer/OrderData` | identical |

## Tests

- `tests/EBICO.Tests/E2E/NegativeSecurityE2ETests.cs` — the three wire-tampering cases per version
  (`061001`/`061001`/`090004`); `RequestTamperingHandler` in `EbicsE2EHarness.cs`.
- `tests/EBICO.Tests/E2E/{Onboarding,Upload,Download}E2ETests.cs` — unchanged green with active
  verification (happy-path evidence of the sign→verify roundtrip).
- `tests/EBICO.Tests/Server/UploadTransactionTests.cs`, `DownloadTransactionTests.cs`,
  `Hca*/Hcs*OrderHandlerTests.cs` — segment/order-data return codes at pipeline level; HCA/HCS
  present a real `AuthSignature` since #58 (`ServerTestHelpers.SignRequestXml`).

## Related documentation

- [E2E: Connector ↔ Server (happy paths)](e2e-connector-server.md) — the base harness (#57)
- [Authentication signature X002](../protocol/auth-signature-x002.md) — the checked crypto primitive
- [EBICS return code catalog](../protocol/return-codes.md) — `061001`/`090004` and the segment codes
- [Upload transaction](../server/upload-transaction.md) / [Download transaction](../server/download-transaction.md) — the segment return codes
- [ADR-0023 — Server-side X002 verification](../adr/0023-server-side-x002-verification.md)
