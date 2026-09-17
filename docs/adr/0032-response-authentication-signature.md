# 0032 — Authentication signature on the server response

- Status: accepted
- Date: 2026-09-17

## Context

Until issue **#143** EBICO answered every request unsigned. The `AuthSignature` element exists in the
generated `ebicsResponse` bindings of all three versions, but nothing ever populated it. This was a
known caveat ("Server responses are unsigned", [#58](../development/negative-security-cases.md)) —
what was not appreciated is that it makes the emulator unusable with a real client rather than merely
less faithful: a client that verifies the bank's signature rejects every response that lacks one.

The failure mode is unusually deceptive. `ebicsKeyManagementResponse` (INI/HIA/HPB) carries **no**
`AuthSignature` element in its schema — those responses precede or bootstrap the key exchange a
signature would be checked against — so a real client skips verification for exactly those three
orders. Onboarding therefore succeeds against an unsigned emulator and *everything after it* fails.
An external evaluation (FSM Cloud) hit precisely that wall: INI/HIA/HPB green, then C53, C5N, CDD,
CDS, HAC, PTK, STA, HTD, HPD and SPR all rejected.

The gap was invisible from inside the repo because the bundled connector did not verify either.
Request signing and verification were symmetric across connector and server ([ADR-0023](0023-server-side-x002-verification.md));
the response direction was absent on both sides, so no test could notice. The primitive itself —
`AuthenticationSignature.Sign`/`Verify` — already existed and is direction-agnostic.

## Decision

Sign the response server-side **and** verify it connector-side, both on by default.

**Server.** A new respond-stage extension point `IEbicsResponseSigner`, defaulting to
`X002EbicsResponseSigner`, which signs with the bank's own key from `IServerBankKeyStore`.
`UnsignedEbicsResponseSigner` is the opt-out. Three sub-decisions:

- **The signer owns the serialisation** (`SerializeAsync(envelope, host) → byte[]`) rather than
  returning a signature the pipeline attaches. Signing is a serialise → sign → re-serialise cycle,
  and a single seam producing the final bytes keeps the pipeline — and the raw-message capture that
  records them — from ever holding a half-signed intermediate.
- **Only `ebicsResponse` is signed.** `IAuthSignedResponseEnvelope` is attached to the three
  `EbicsResponse` bindings and deliberately *not* to `EbicsKeyManagementResponse`, so the split is a
  type-level fact rather than a runtime condition.
- **An unresolvable or unknown host yields an unsigned response.** The bank key pair is per `HostId`.
  Malformed XML carries no `HostID` at all, and for an unknown one the signer consults the master data
  before touching the key store — mirroring `HpbOrderHandlerBase`. Otherwise a request with a made-up
  `HostID` would make the emulator generate an RSA key pair, which is cheap denial-of-service against
  a process meant to be left running.

**Connector.** `ResponseSignatureVerifier` in the shared `ExchangeAsync` of the upload and download
paths, verifying against the bank key HPB stored. A missing or invalid signature raises
`EbicsResponseSignatureException` rather than surfacing as a return code: a response that cannot be
attributed to the bank contains no trustworthy return code to report. Switchable per connection via
`EbicsConnectionOptions.VerifyResponseSignature`.

## Consequences

- The emulator is usable with clients that verify bank responses — the finding that motivated this is
  reproduced as an E2E test (`ResponseSignatureE2ETests`) and passes with the signer in place.
- **The connector now verifies too**, which is what makes the server's signature testable at all:
  without it, only self-consistency could be asserted. It also removes a real asymmetry — the bundled
  client was less strict than a third-party one.
- **Every Tier-A connector fake server had to start signing** (`FakeBankIdentity`). That is the right
  shape: 102 download/upload tests now exercise verification incidentally, so a regression in the
  signature path goes red broadly rather than in one suite.
- Existing server-side tests are unaffected: they assert on response *content*, and `AuthSignature` is
  an addition between `header` and `body`.
- **Byte-level interop remains unevidenced.** The round-trip is self-consistent across H003/H004/H005,
  but whether the reference URI, its XPath realisation and the C14N context match what a real bank
  emits is still the open [X002 spec caveat](../protocol/auth-signature-x002.md#spec-caveat). A
  captured real-bank response in H004 is the way to close it — the approach of
  [#59](../development/conformance-real-clients.md).

## Alternatives

- **Sign in `EbicsResponseFactory`:** rejected. The factory builds envelopes and knows nothing about
  hosts or key stores; it is also used by test fakes that must be able to produce unsigned output.
- **Return a `SignatureType` and let the pipeline attach and serialize it:** rejected. The pipeline
  would then serialize twice and own the ordering constraint between signing and capture — the exact
  coupling the extension point is meant to absorb.
- **Sign `ebicsKeyManagementResponse` too:** impossible without deviating from the schema, and wrong
  in substance — the subscriber holds no bank key before HPB completes.
- **Mint a bank key pair for any `HostID` (drop the master-data check):** rejected as an unbounded
  RSA-generation vector, and meaningless besides: signing as a host that does not exist authenticates
  nothing.
- **Server-side only, leave the connector unchanged:** rejected. It would ship an untested signature
  and keep the bundled client weaker than the third-party clients EBICO exists to emulate a bank for.
- **Connector verification off by default:** rejected. A default that accepts unauthenticated
  responses is the wrong default for a library that talks to banks; the per-connection opt-out covers
  the server-does-not-sign case.
