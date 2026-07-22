---
name: ebics-conformance-test
description: >-
  Anleitung zum Schreiben oder Erweitern von End-to-End- und Conformance-Tests in EBICO.Tests.
  Verwenden bei einem echten Round-Trip zwischen EBICO.Connector und EBICO.Server, bei Wire-Shape-
  Varianten (legitime Fremd-Client-XML-Formen), Vendor-Capture-Vergleichen oder Tampering-/Negativ-
  Sicherheitsfällen. Deckt EbicsE2EHarness, E2EKeyPool, WireShape/XmlShape, VendorCaptureCorpus,
  RequestTamperingHandler, die Versionsmatrix H003/H004/H005 und die erwarteten Returncodes ab.
---

# E2E-/Conformance-Test schreiben

Zwei Test-Ebenen unter `tests/EBICO.Tests/`:

- **E2E** (`E2E/`): echter Round-Trip — die echte Connector-Pipeline spricht über
  `WebApplicationFactory<Program>` gegen die echte Server-Pipeline (Happy Paths #57, Negativ/Security #58).
- **Conformance** (`Conformance/`): dieselbe Harness, aber mit legitimen **Wire-Format-Varianten**
  und **Vendor-Captures**, um Spec-Konformität statt nur EBICO↔EBICO-Konsistenz zu belegen (#59).

Zuerst `docs/development/testing.md`, `docs/development/e2e-connector-server.md` und
`docs/development/negative-security-cases.md` lesen.

## Gerüst

- **Program-Kollision auflösen:** `extern alias EbicoServer;` + `using ServerProgram = EbicoServer::Program;`,
  dann `IClassFixture<WebApplicationFactory<ServerProgram>>`.
- **Harness:** `EbicsE2EHarness` (`tests/EBICO.Tests/E2E/EbicsE2EHarness.cs`) verdrahtet Connector-Pipeline
  gegen `factory.Server.CreateHandler()`. **Stolperstein:** der echte `HttpClientTransport` postet gegen
  die *absolute* `EbicsConnection.Url`, nicht die `BaseAddress`.
- **Schlüssel:** `E2EKeyPool` — RSA-2048 ist harte Untergrenze in `RsaKeyMaterial`, daher Schlüssel
  wiederverwenden statt kleinere zu erzeugen (Testlaufzeit).
- **Isolation:** je Test eine eigene `HostID` statt eines eigenen Hosts; Teilnehmer bewusst im
  Zustand `New` seeden.
- **CancellationToken:** immer `TestContext.Current.CancellationToken` übergeben (xUnit1051 unter
  `TreatWarningsAsErrors`).

## Versionsmatrix & Wire-Shapes

- Fälle als `TheoryData<...>` × H003/H004/H005 aufspannen, per `[MemberData]` einspeisen
  (Vorlage: `Conformance/OnboardingWireShapeConformanceTests.cs`).
- **`XmlShape` / WireShape** (`Conformance/XmlShape.cs`): Mutatoren, die legitime Fremd-Client-Varianten
  erzeugen (Reindent, Kommentare injizieren, anderes Root-Präfix). Beleg: der Server keyed auf die
  **Namespace-URI**, nicht auf das EBICO-Präfix.
- **`VendorCaptureCorpus`** (`Conformance/VendorCaptureCorpus.cs`): lädt committete Vendor-Captures aus
  `Conformance/Vendor/<client>/<version>/<direction>/`. OSS-Client-Output ist committfähig; fehlt das
  Korpus, degradiert der Test graceful (kein Hard-Fail).
- **`VendorCaptureConformanceTests`**: seit #117 ein **sequenzieller** Positivtest (ein `[Fact]`, kein
  `[Theory]`) — die Captures sind eine Kette INI → HIA → HPB und jeder Schritt ist Vorbedingung des
  nächsten. Vorher die im Capture verwendeten IDs als Stammdaten seeden
  (`IMasterDataManager` aus `factory.Services`; Subscriber in `SubscriberState.New` lassen, das
  Onboarding treibt ihn nach `Ready`) — **keine** Schlüssel vorseeden, die kommen aus den Captures.

## Negativ-/Tampering-Fälle

- `RequestTamperingHandler` (in der Harness) manipuliert die Wire-XML scharf **nach** dem Onboarding.
- Erwartete Returncodes: manipulierte `SignatureValue`/authentifizierter Header (`NumSegments`) → **`061001`**
  (`EBICS_AUTHENTICATION_FAILED`, X002 schützt den ganzen `authenticate="true"`-Header); manipuliertes,
  nicht authentifiziertes `OrderData` → **`090004`** (überlebt die Signatur, scheitert bei der Entschlüsselung).
- Weitere gängige Codes: Happy Download-Receipt `011000` (nicht `000000`), Reihenfolge `091002`,
  Berechtigung `090003`, ungültige pain.001 `090004`.
- Serverseitige X002-Verifikation aktivieren via `X002EbicsRequestVerifier` (Default seit ADR-0023);
  für reine Ablauf-Tests ohne Signaturprüfung `NoOpEbicsRequestVerifier` vor `AddEbicoServer` substituieren.

## Assertions & Doku

- XML strukturell mit `CanonicalXmlComparer` (`tests/EBICO.Tests/Infrastructure/`) vergleichen, nicht per String.
- Proprietäre Sample-XML „skip-if-missing" laden (nicht im Repo).
- **Pflicht:** die Test-XML-Doc enthält einen expliziten **„Spec-Vorbehalt"**-Absatz (was NICHT geprüft wird:
  ES/A00x, ggf. unsignierte Antwort, synthetische Daten, Gegenstelle = Emulator).

## Quellen

- Code: `tests/EBICO.Tests/{E2E,Conformance,Infrastructure,Fixtures}` (u. a. `OnboardingE2ETests`,
  `NegativeSecurityE2ETests`, `OnboardingWireShapeConformanceTests`, `WireShapeNegativeConformanceTests`,
  `SignedRequestCanonicalizationConformanceTests`, `SchemaValidationConformanceTests`).
- Doku: `docs/development/testing.md`, `docs/development/e2e-connector-server.md`,
  `docs/development/negative-security-cases.md`. ADR: 0023 (serverseitige X002-Verifikation).
