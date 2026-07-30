---
name: ebics-conformance-test
description: >-
  Guide to writing or extending end-to-end and conformance tests in EBICO.Tests.
  Use for a real round-trip between EBICO.Connector and EBICO.Server, for wire shape
  variants (legitimate third-party client XML forms), vendor capture comparisons or tampering/negative
  security cases. Covers EbicsE2EHarness, E2EKeyPool, WireShape/XmlShape, VendorCaptureCorpus,
  RequestTamperingHandler, the H003/H004/H005 version matrix and the expected return codes.
---

# Writing an E2E/conformance test

Two test levels under `tests/EBICO.Tests/`:

- **E2E** (`E2E/`): real round-trip — the real connector pipeline talks through
  `WebApplicationFactory<Program>` to the real server pipeline (happy paths #57, negative/security #58).
- **Conformance** (`Conformance/`): the same harness, but with legitimate **wire format variants**
  and **vendor captures**, to evidence spec conformance rather than mere EBICO↔EBICO consistency (#59).

Read `docs/development/testing.md`, `docs/development/e2e-connector-server.md` and
`docs/development/negative-security-cases.md` first.

## Scaffolding

- **Resolve the Program collision:** `extern alias EbicoServer;` + `using ServerProgram = EbicoServer::Program;`,
  then `IClassFixture<WebApplicationFactory<ServerProgram>>`.
- **Harness:** `EbicsE2EHarness` (`tests/EBICO.Tests/E2E/EbicsE2EHarness.cs`) wires the connector pipeline
  to `factory.Server.CreateHandler()`. **Pitfall:** the real `HttpClientTransport` posts to the
  *absolute* `EbicsConnection.Url`, not to the `BaseAddress`.
- **Keys:** `E2EKeyPool` — RSA-2048 is a hard lower bound in `RsaKeyMaterial`, so reuse keys instead of
  generating smaller ones (test runtime).
- **Isolation:** one dedicated `HostID` per test instead of a dedicated host; seed subscribers
  deliberately in state `New`.
- **CancellationToken:** always pass `TestContext.Current.CancellationToken` (xUnit1051 under
  `TreatWarningsAsErrors`).

## Version matrix & wire shapes

- Span the cases as `TheoryData<...>` × H003/H004/H005 and feed them in via `[MemberData]`
  (template: `Conformance/OnboardingWireShapeConformanceTests.cs`).
- **`XmlShape` / WireShape** (`Conformance/XmlShape.cs`): mutators that produce legitimate third-party
  client variants (reindent, inject comments, different root prefix). Evidence: the server keys on the
  **namespace URI**, not on the EBICO prefix.
- **`VendorCaptureCorpus`** (`Conformance/VendorCaptureCorpus.cs`): loads committed vendor captures from
  `Conformance/Vendor/<client>/<version>/<direction>/`. OSS client output is committable; if the corpus
  is missing, the test degrades gracefully (no hard fail).
- **`VendorCaptureConformanceTests`**: since #117 a **sequential** positive test (one `[Fact]`, no
  `[Theory]`) — the captures are a chain INI → HIA → HPB and each step is a precondition of the
  next. Seed the IDs used in the capture as master data beforehand
  (`IMasterDataManager` from `factory.Services`; leave the subscriber in `SubscriberState.New`, the
  onboarding drives it to `Ready`) — do **not** pre-seed keys, those come from the captures.

## Negative/tampering cases

- `RequestTamperingHandler` (in the harness) manipulates the wire XML for real **after** the onboarding.
- Expected return codes: tampered `SignatureValue`/authenticated header (`NumSegments`) → **`061001`**
  (`EBICS_AUTHENTICATION_FAILED`, X002 protects the whole `authenticate="true"` header); tampered,
  unauthenticated `OrderData` → **`090004`** (survives the signature, fails at decryption).
- Other common codes: happy download receipt `011000` (not `000000`), ordering `091002`,
  authorisation `090003`, invalid pain.001 `090004`.
- Enable server-side X002 verification via `X002EbicsRequestVerifier` (the default since ADR-0023);
  for pure flow tests without signature checking, substitute `NoOpEbicsRequestVerifier` before `AddEbicoServer`.

## Assertions & docs

- Compare XML structurally with `CanonicalXmlComparer` (`tests/EBICO.Tests/Infrastructure/`), not as strings.
- Load proprietary sample XML "skip-if-missing" (not in the repo).
- **Mandatory:** the test XML-doc contains an explicit **"spec caveat"** paragraph (what is NOT checked:
  ES/A00x, possibly the unsigned response, synthetic data, counterparty = emulator).

## Sources

- Code: `tests/EBICO.Tests/{E2E,Conformance,Infrastructure,Fixtures}` (among others `OnboardingE2ETests`,
  `NegativeSecurityE2ETests`, `OnboardingWireShapeConformanceTests`, `WireShapeNegativeConformanceTests`,
  `SignedRequestCanonicalizationConformanceTests`, `SchemaValidationConformanceTests`).
- Docs: `docs/development/testing.md`, `docs/development/e2e-connector-server.md`,
  `docs/development/negative-security-cases.md`. ADR: 0023 (server-side X002 verification).
