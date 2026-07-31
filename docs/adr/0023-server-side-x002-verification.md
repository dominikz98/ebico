# 0023 — Server-side X002 authentication-signature verification

- Status: accepted
- Date: 2026-07-17

## Context

Until Milestone M8, the *verify* stage of the server pipeline was a no-op
(`NoOpEbicsRequestVerifier`): the connector signed every `ebicsRequest` with X002, but
the server did not check the signature. As a result the authentication signature was
tested in **no** direction — a gap that issue **#58** (negative/security cases) is meant
to close. The crypto primitive
[`AuthenticationSignature.Verify`](../protocol/auth-signature-x002.md) and the envelope
abstraction `IAuthSignedRequestEnvelope` already existed; what was missing was the
wiring in the server (subscriber resolution + key lookup + error-code path).

Complicating matters: transfer/receipt requests carry only the HostID in the header
(the subscriber bound to the transaction), and before HIA no auth key exists server-side
against which to check at all. A naive "always verify strictly" variant would have broken
many existing server tests (hand-built, unsigned XML) as well as the onboarding
bootstrap.

## Decision

A productive `X002EbicsRequestVerifier` replaces the no-op as the default
(`AddEbicoServer`, still swappable via `TryAddSingleton`). Its behaviour:

- **Only signed `ebicsRequest`** are checked (upload init/transfer, download
  init/transfer/receipt, HCA/HCS/SPR). `ebicsUnsecuredRequest` (INI/HIA/HSA) and
  `ebicsNoPubKeyDigestsRequest` (HPB) are skipped.
- **Subscriber resolution:** from the static-header triple (init/single-phase) or via the
  upload/download transaction store (transfer/receipt, only HostID in the header).
- **Verification only when an auth key is present** (after HIA). Without a key: `Success`
  — the state machine rejects premature orders with `091002`. With a key a valid
  `AuthSignature` is mandatory; absence/failure → `EBICS_AUTHENTICATION_FAILED` (`061001`,
  technical → header).

Tested end-to-end by the negative suite (`NegativeSecurityE2ETests`, wire tampering) and
implicitly by the unchanged happy-path E2E, which now verify a real connector signature
server-side. See [negative/security cases](../development/negative-security-cases.md).

## Consequences

- The server rejects tampered/wrongly signed `ebicsRequest` by default with `061001`; the
  entire `authenticate="true"` header (incl. segment metadata) is thereby protected.
- **Blast radius on existing tests minimal:** only the two HCA/HCS happy-path tests that
  seed an auth key **and** sent an unsigned request had to be adjusted — they now sign via
  `ServerTestHelpers.SignRequestXml` with the stored auth key. All other server tests seed
  no auth key and therefore fall into the `Success` branch.
- The verification is **deliberately conditional** (only when a key is present): this
  models the EBICS bootstrap (no check possible before HIA) and keeps the onboarding flows
  and wire-level server tests functional. An attacker signing as a non-onboarded subscriber
  fails at the state machine anyway (`091002`).

## Alternatives

- **Leave the no-op (status quo):** rejected — the signature would stay untested, #58 not
  fulfillable.
- **Always verify strictly (signature mandatory on every `ebicsRequest`, regardless of key
  status):** rejected — breaks the onboarding bootstrap and the unsigned wire-level server
  tests, without security gain (the state machine already catches keyless subscribers).
- **Verification only at the initialisation phase (transfer/receipt unchecked):** considered
  as a fallback; rejected, because the header is signed there too and the transaction-store
  resolution was conveniently available — full verification is the more coherent choice.
