# Response signature X002 (server → subscriber)

The bank's **authentication signature on the way back**: the server signs every transaction
`ebicsResponse` with its own X002 key, and the connector verifies that signature against the bank key
it fetched over HPB before acting on the content. The outbound mirror image of the inbound check
([#58](../development/negative-security-cases.md), [ADR-0023](../adr/0023-server-side-x002-verification.md)),
using the same primitive ([X002](auth-signature-x002.md)) in the other direction.

Issue **#143**, decision: [ADR-0032](../adr/0032-response-authentication-signature.md).

> **Why this exists.** A real bank signs its responses and real clients verify them. Until #143 EBICO
> answered unsigned, which a client that checks rejects — so onboarding (INI/HIA/HPB) went through and
> *every* order after it failed. The gap was invisible from inside the repo because the bundled
> connector did not verify either: request signing and verification were symmetric, response signing
> was absent on both sides. It surfaced in an external evaluation (FSM Cloud) against a client that
> does check.

## What is signed, and what is not

| Envelope | Signed | Why |
|---|---|---|
| `ebicsResponse` (initialisation, transfer, receipt, single-phase orders) | **yes** | Carries the `AuthSignature` element in its schema, between `header` and `body`. |
| `ebicsKeyManagementResponse` (INI/HIA/HPB) | **no** | Its schema has **no** `AuthSignature` element — these responses precede or bootstrap the very key exchange a signature would be checked against. |

That split is not a simplification: it is why an evaluation can get all the way through onboarding
against an emulator that never signs, and only then discover that nothing else works.

The authenticated node-set is the one the generated bindings mark — the response `header` plus the
return-code, timestamp and encryption-info elements that carry `authenticate="true"`. `AuthSignature`
is *not* part of it, so signing an already-signed envelope re-signs over the unsigned form and
reproduces the same bytes (guarded by a test).

## Server side

`IEbicsResponseSigner` is the respond-stage extension point in
[`EbicsRequestPipeline`](../server/host.md#request-pipeline). It owns the **serialisation**, because
signing is a serialise → sign → re-serialise cycle and the pipeline (and the raw-message capture that
records the bytes) must never see a half-signed intermediate.

| Type | Role |
|---|---|
| `X002EbicsResponseSigner` | The default. Signs with the bank's own key from `IServerBankKeyStore`. |
| `UnsignedEbicsResponseSigner` | Opt-out: serializes every envelope as built. |

```csharp
// Opt out before AddEbicoServer (TryAdd defaults yield to a prior registration):
services.TryAddSingleton<IEbicsResponseSigner, UnsignedEbicsResponseSigner>();
services.AddEbicoServer();
```

The bank key pair is **per `HostId`**, so the host has to be resolvable to sign. A response goes out
**unsigned** in two cases:

- **No host in the request** — malformed XML never parses far enough to carry a `HostID`, so there is
  no bank identity to sign as.
- **Host not in the master data** — mirroring `HpbOrderHandlerBase`, the key pair is only materialized
  for a host that exists. Otherwise every request with a made-up `HostID` would make the emulator
  generate an RSA key pair, which is a cheap denial-of-service against an emulator that is meant to be
  left running.

Both are error responses by construction; a client that verifies will reject them, which is the
correct outcome for a message the server cannot authenticate itself as the sender of.

A **public-only** bank authentication key is a third case, and the only one that fails loudly:
`IServerBankKeyStore.SetAsync` accepts such a pair (the public part alone serves HPB), but it cannot
sign, so the signer throws with an actionable message instead of answering unsigned. Silently
answering unsigned there would reintroduce the very defect this exists to remove, and hide it.

## Connector side

`ResponseSignatureVerifier` runs inside the shared `ExchangeAsync` of the upload and download paths,
so every transaction response passes through it. It reads the bank's X002 key from the connector key
store (`KeyOwner.Bank`, `KeyPurpose.Authentication`) — where HPB left it.

| Outcome | Result |
|---|---|
| Signature verifies | The caller proceeds to ordinary return-code handling. |
| Response unsigned | `EbicsResponseSignatureException` |
| Signature invalid (wrong key, altered in transit) | `EbicsResponseSignatureException` |
| No bank key stored | `EbicsConfigurationException` — HPB has not run. |
| Key-management response, or a body that does not parse as an envelope | Not checked; left to the caller's own parsing. |

A failed signature is an **exception, not a return code**: the response cannot be attributed to the
bank at all, so there is no trustworthy return code inside it to report.

To talk to a server that does not sign:

```csharp
services.AddEbicoConnector(o =>
{
    // …
    o.VerifyResponseSignature = false;   // the response is then unauthenticated
});
```

## Spec caveat

> **⚠️ Spec caveat:** which nodes the response authenticates follows the generated bindings'
> `authenticate="true"` defaults, and the canonicalisation details are those documented for
> [X002](auth-signature-x002.md#spec-caveat) — the reference URI
> `#xpointer(//*[@authenticate='true'])`, its XPath realisation, inclusive C14N and the SignedInfo
> canonicalisation context. None of it is verified against the official annexes (the XSDs are
> proprietary, see `CLAUDE.md`).
>
> The connector↔server round-trip is self-consistent and the E2E suite evidences it across
> H003/H004/H005, but **byte-level interop with a third-party client is not yet evidenced**. The way
> to close that is a captured request/response pair from a real bank in H004 to verify the signature
> against — the same approach as [#59](../development/conformance-real-clients.md). Until then, a
> client that rejects EBICO's response signature is as likely to indicate a wrong detail here as a
> fault on its side.

Also still open: the server's keys carry **no validity window**, and the **ES/A00x order signature**
of uploaded order data remains unverified — see
[Negative & security cases](../development/negative-security-cases.md).

## Tests

| Suite | What it evidences |
|---|---|
| `Server/X002EbicsResponseSignerTests` | Signature produced and verifiable per version; wrong key and tampered header rejected; key-management response, missing host and unknown host stay unsigned; unknown host mints no key pair; re-signing is stable. |
| `Connector/ResponseSignatureVerificationTests` | Unsigned, foreign-key-signed and tampered responses never reach the caller; opt-out works; missing bank key is a configuration error. |
| `E2E/ResponseSignatureE2ETests` | Real connector ↔ real server: every phase of a C53 download is signed with the key the client fetched over HPB. With the signer swapped out, onboarding still succeeds and the business order fails — the reported finding, reproduced. |
| Every other download/upload suite | Their fake servers sign as the bank does, so verification is exercised throughout rather than only in its own suite. |

## Related

- [Authentication signature X002](auth-signature-x002.md) — the primitive both directions use.
- [ADR-0032](../adr/0032-response-authentication-signature.md) — why the signer owns serialisation, and why an unknown host is not signed for.
- [ADR-0023](../adr/0023-server-side-x002-verification.md) — the inbound counterpart.
- [Negative & security cases](../development/negative-security-cases.md) — the remaining caveats.
- [Connector architecture](../connector/architecture.md) — where verification sits in the send pipeline.
