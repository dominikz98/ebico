# E2E: Negativ- & Sicherheitsfälle

> Umsetzung von **Issue #58** (Milestone M8 — Validation & Conformance). Baut auf der
> E2E-Harness aus [#57](e2e-connector-server.md) auf und ergänzt sie um zwei Dinge: die
> **produktive Prüfung der X002-Authentifikationssignatur** im Server und eine
> **Negativsuite**, die einen echten, signierten Request auf dem Draht manipuliert.
>
> Bewusst **enthalten**: serverseitige X002-Verifikation jedes signierten `ebicsRequest`;
> E2E-Nachweis, dass eine manipulierte Signatur, ein manipulierter (signierter) Header und
> manipuliertes OrderData abgelehnt werden — je **H003/H004/H005**.
>
> Bewusst **noch nicht** (dokumentierte Spec-Vorbehalte, siehe unten): serverseitige Prüfung
> **abgelaufener Keys**, Verifikation der **ES/A00x**-Ordersignatur, Signatur der
> Server-**Antworten**.

## Zweck

Bis #57 erzeugte der Connector zwar eine X002-Signatur je `ebicsRequest`, der Server prüfte sie
aber nicht (`NoOpEbicsRequestVerifier`). Damit war die Authentifikationssignatur in **keiner**
Richtung getestet. #58 schaltet die Prüfung produktiv ein und belegt sie end-to-end: erst dadurch
sind „falsche Signatur" und „manipuliertes OrderData" überhaupt als reale Ablehnungen prüfbar —
nicht nur als selbstkonsistente Krypto-Primitive.

Die tragende Beobachtung ist, **was** die X002-Signatur schützt:

- Der gesamte `EbicsRequestHeader` ist `authenticate="true"` (ebenso `DataEncryptionInfo`,
  `SignatureData`, `TransferReceipt`). Jede Manipulation am Header — inklusive `NumSegments`,
  `TransactionID`, `SegmentNumber` — bricht den Reference-Digest und wird mit **`061001`**
  `EBICS_AUTHENTICATION_FAILED` abgelehnt.
- Die `OrderData` selbst ist **nicht** authentifiziert (sie ist E002-verschlüsselt). Manipulation
  am Chiffrat übersteht die Signaturprüfung und scheitert erst bei der Entschlüsselung/Dekompression
  mit **`090004`** `EBICS_INVALID_ORDER_DATA_FORMAT`.

## Serverseitige X002-Verifikation

`src/EBICO.Server/Pipeline/X002EbicsRequestVerifier.cs` ersetzt den `NoOpEbicsRequestVerifier` als
Default (`AddEbicoServer`; weiterhin per `TryAddSingleton` austauschbar). Die Pipeline ruft den
Verifier für **jeden** Request auf (`EbicsRequestPipeline`, Stage *Verify*), die Auswahl liegt also
im Verifier:

1. **Nur signierte `ebicsRequest`** werden geprüft (Upload-Init/-Transfer, Download-Init/-Transfer/
   -Receipt, HCA/HCS/SPR). `ebicsUnsecuredRequest` (INI/HIA/HSA) und `ebicsNoPubKeyDigestsRequest`
   (HPB) werden übersprungen — sie leiten den Schlüsseltausch erst ein bzw. bootstrappen ihn.
2. **Subscriber-Auflösung:** Init-/Einphasen-Requests tragen das Tripel (HostID/PartnerID/UserID) im
   Static-Header; Transfer-/Receipt-Requests tragen nur die HostID, der Teilnehmer ist an die
   Transaktion gebunden und wird über den Upload-/Download-Transaction-Store aufgelöst.
3. **Verifikation läuft nur bei vorhandenem Auth-Key** (nach HIA). Vorher gibt es nichts zu prüfen;
   ein verfrühter Auftrag wird ohnehin vom Zustandsautomaten abgelehnt (`091002`). Ist ein Key
   hinterlegt, ist eine gültige `AuthSignature` **Pflicht** — Fehlen oder Fehlschlag →
   `EbicsVerificationResult.Fail(EbicsReturnCode.AuthenticationFailed)` (`061001`, technisch → Header).

Verifiziert wird über die vorhandene Krypto-Primitive
[`AuthenticationSignature.Verify`](../protocol/auth-signature-x002.md) gegen den im `IServerKeyStore`
gespeicherten X002-Schlüssel des Teilnehmers.

## Abgedeckte Fälle

Die Negativsuite (`tests/EBICO.Tests/E2E/NegativeSecurityE2ETests.cs`) fährt einen echten CCT-Upload
und manipuliert den bereits signierten Request über einen `RequestTamperingHandler` (sitzt oberhalb
des Transport-Handlers, scharfgeschaltet **nach** dem Onboarding, damit INI/HIA/HPB unberührt bleiben).

| Fall | H003 | H004 | H005 | Erwartung |
| --- | :---: | :---: | :---: | --- |
| Manipulierte `SignatureValue` (Init) | ✅ | ✅ | ✅ | `061001` `EBICS_AUTHENTICATION_FAILED` |
| Manipulierter Header `NumSegments` (Init) | ✅ | ✅ | ✅ | `061001` — X002 schützt die Segment-Metadaten |
| Manipuliertes `OrderData` (Transfer) | ✅ | ✅ | ✅ | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` |

3 Theories × 3 Versionen = **9 Round-Trips**. Der Happy-Path-Beleg (dass eine **korrekte**
Connector-Signatur serverseitig verifiziert) liegt in den unveränderten #57-Suiten
(`OnboardingE2ETests`/`UploadE2ETests`/`DownloadE2ETests`) — sie bleiben grün und schließen damit
den Sign→Verify-Roundtrip über die Grenze Connector-Serialisierung ↔ Server-C14N.

## Returncodes & Fehlerfälle

| Situation | Returncode |
| --- | --- |
| Manipulierte Signatur / manipulierter authentifizierter Header | `061001` `EBICS_AUTHENTICATION_FAILED` |
| Manipuliertes (nicht authentifiziertes) OrderData-Chiffrat | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` |

Die klassischen Segment-Inkonsistenz-Returncodes verlangen einen **gültig signierten, aber logisch
inkonsistenten** Request — den der Connector nie erzeugt (und den X002 auf dem Draht als `061001`
abfängt). Sie sind daher auf **Server-Pipeline-Ebene** abgedeckt (hand-gebautes, unsigniertes XML
gegen einen verify-übersprungenen Teilnehmer):

| Situation | Returncode | Test |
| --- | --- | --- |
| Doppeltes Segment (Replay) | `091103` `EBICS_TX_MESSAGE_REPLAY` | `Server/UploadTransactionTests` |
| `lastSegment` vor Vollständigkeit (Underrun) | `011101` `EBICS_TX_SEGMENT_NUMBER_UNDERRUN` | `Server/UploadTransactionTests` |
| Segmentnummer > `NumSegments` | `091104` `EBICS_TX_SEGMENT_NUMBER_EXCEEDED` | `Server/UploadTransactionTests` |
| Unbekannte/abgelaufene Transaktions-ID | `091101` `EBICS_TX_UNKNOWN_TXID` | `Server/{Upload,Download}TransactionTests` |
| Undecryptierbares/undecompressierbares OrderData | `090004` `EBICS_INVALID_ORDER_DATA_FORMAT` | `Server/UploadTransactionTests`, `Server/Hca*Tests` |

### ⚠️ Spec-Vorbehalte

- **Abgelaufene Keys werden serverseitig nicht geprüft.** Für Teilnehmer-RSA-Schlüssel speichert der
  Server kein Gültigkeitsfenster (`StoredPublicKey` = Modulus/Exponent + KeyVersion). Das Primitive
  `X509CertificateVerifier.RefineValidity` (Ablauf-/Not-Yet-Valid-Erkennung) existiert, wird aber nur
  **clientseitig** (HPB-Bankzertifikat) genutzt. Serverseitige Ablaufprüfung ist Key-Management-Scope
  (M3/M4) und bewusst nicht Teil von #58.
- **ES/A00x-Ordersignatur bleibt ungeprüft.** Die *autorisierende* banktechnische Signatur der
  OrderData wird weiterhin nur mitgeführt, nicht verifiziert.
- **Server-Antworten sind unsigniert.** #58 prüft die Request-Richtung; die Signatur der
  `ebicsResponse` bleibt ein offener Vorbehalt (M4/M6).
- **C14N-Vorbehalt fort:** Der byte-genaue Kanonisierungs-/Reference-Detail von X002 ist weiterhin
  nicht gegen die offiziellen Annexe verifiziert (siehe [X002-Doku](../protocol/auth-signature-x002.md)).
  Der Connector↔Server-Roundtrip ist in sich konsistent (die Happy-Path-E2E belegen ihn), die
  Interop gegen reale Clients ist Gegenstand von
  [#59](conformance-real-clients.md).

## EBICS-Versionsbezug

Das Verfahren ist über H003/H004/H005 identisch: geprüft wird die C14N der `authenticate="true"`-Knoten
(v. a. der `header`) plus die RSA-Signatur über `SignedInfo`. Die Versionen unterscheiden sich nur in
der Einreichungs-Konvention der Order (H003/H004 `OrderType`, H005 `AdminOrderType`+BTF) — für die
Signaturprüfung irrelevant, da der gesamte Header signiert ist. Ein Berechtigungssatz und ein
Auth-Key je Teilnehmer decken alle drei Versionen ab.

| Aspekt | H003 / H004 | H005 |
| --- | --- | --- |
| Authentifizierter Bereich | `header` (`authenticate="true"`) + Krypto-Metadaten | identisch |
| Auth-Schlüsselversion | `X002` | `X002` |
| Nicht authentifiziert | `body/DataTransfer/OrderData` | identisch |

## Tests

- `tests/EBICO.Tests/E2E/NegativeSecurityE2ETests.cs` — die drei Wire-Tampering-Fälle je Version
  (`061001`/`061001`/`090004`); `RequestTamperingHandler` in `EbicsE2EHarness.cs`.
- `tests/EBICO.Tests/E2E/{Onboarding,Upload,Download}E2ETests.cs` — unverändert grün mit aktiver
  Verifikation (Happy-Path-Beleg des Sign→Verify-Roundtrips).
- `tests/EBICO.Tests/Server/UploadTransactionTests.cs`, `DownloadTransactionTests.cs`,
  `Hca*/Hcs*OrderHandlerTests.cs` — Segment-/OrderData-Returncodes auf Pipeline-Ebene; HCA/HCS
  präsentieren seit #58 eine echte `AuthSignature` (`ServerTestHelpers.SignRequestXml`).

## Verwandte Doku

- [E2E: Connector ↔ Server (Happy Paths)](e2e-connector-server.md) — die Basis-Harness (#57)
- [Authentifikationssignatur X002](../protocol/auth-signature-x002.md) — die geprüfte Krypto-Primitive
- [EBICS-Returncode-Katalog](../protocol/return-codes.md) — `061001`/`090004` und die Segment-Codes
- [Upload-Transaktion](../server/upload-transaction.md) / [Download-Transaktion](../server/download-transaction.md) — die Segment-Returncodes
- [ADR-0023 — Serverseitige X002-Verifikation](../adr/0023-serverseitige-x002-verifikation.md)
