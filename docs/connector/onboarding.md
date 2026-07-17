# Connector: Onboarding-Flows INI / HIA / HPB

> Umsetzung von **Issue #47** (Milestone M6 — Connector). Diese Seite beschreibt die
> clientseitigen Onboarding-Flows des `EBICO.Connector`: die Schlüsselgenerierung, das Senden von
> INI und HIA, das Abrufen und Verifizieren der Bankschlüssel per HPB sowie den INI-/HIA-Brief.
> Grundlage ist der [Client-Kern](client-core.md) (#46); der Gesamtentwurf steht in der
> [Connector-Architektur](architecture.md).

## Zweck

Bevor ein Teilnehmer fachliche Aufträge (Upload/Download) senden kann, muss der Schlüsselaustausch
abgeschlossen sein:

1. **Schlüsselgenerierung** — der Teilnehmer erzeugt drei RSA-Paare: Signatur (`A00x`),
   Authentifikation (`X002`) und Verschlüsselung (`E002`).
2. **INI** — sendet den öffentlichen **Signaturschlüssel** (ungesichert).
3. **HIA** — sendet die öffentlichen **Authentifikations- und Verschlüsselungsschlüssel** (ungesichert).
4. **INI-/HIA-Brief** — wird ausgedruckt, unterschrieben und (per Post/Fax) an die Bank übermittelt.
   Die Bank gleicht die darin gedruckten Schlüssel-Hashes mit den per INI/HIA empfangenen ab.
5. **HPB** — der Teilnehmer holt die öffentlichen **Bankschlüssel** (X002/E002) ab, verifiziert sie
   gegen den Bankbrief (Hash-Abgleich) und legt sie ab.

```mermaid
sequenceDiagram
    participant C as Teilnehmer (Connector)
    participant S as EBICS-Server (Bank)
    C->>S: INI — öffentlicher A00x-Signaturschlüssel (ungesichert)
    S-->>C: Returncode
    C->>S: HIA — öffentliche X002/E002-Schlüssel (ungesichert)
    S-->>C: Returncode
    Note over C,S: INI-/HIA-Brief mit Schlüssel-Hashes manuell zur Bank;<br/>Bank aktiviert den Teilnehmer.
    C->>S: HPB — Abruf der Bankschlüssel (X002-signiert)
    S-->>C: X002/E002 der Bank (E002-verschlüsselt)
    Note over C: Bank-Hashes gegen Bankbrief verifizieren, dann im IKeyStore ablegen.
```

## Öffentliche API

Alle Requests folgen dem Mediator-Muster (`IEbicsRequest<TResult>`; siehe [Client-Kern](client-core.md)):

```csharp
services.AddEbicoConnector(o => { /* Url, HostId, PartnerId, UserId, Version */ })
        .Services.AddEbicoOnboarding();

// 1. Schlüssel erzeugen (einmalig, außerhalb der Send-Pipeline).
var keys = await keyGenerator.GenerateAsync();          // ISubscriberKeyGenerator

// 2./3. INI + HIA senden.
var ini = await client.Send(new IniRequest());          // -> IniResult (mit Brief)
var hia = await client.Send(new HiaRequest());          // -> HiaResult (mit Brief)

// 4. Brief ausgeben (Text + PDF).
File.WriteAllText("ini-brief.txt", ini.Value!.Letter!.Text);
File.WriteAllBytes("ini-brief.pdf", ini.Value!.Letter!.Pdf!);

// 5. Bankschlüssel abrufen + gegen den Bankbrief verifizieren.
var hpb = await client.Send(new HpbRequest
{
    ExpectedAuthenticationKeyDigest = bankLetterAuthDigest,
    ExpectedEncryptionKeyDigest     = bankLetterEncDigest,
});
```

| Typ | Rolle |
| --- | --- |
| `ISubscriberKeyGenerator` | Erzeugt A00x/E002/X002, legt sie im `IKeyStore` (`KeyOwner.Subscriber`) ab. **Explizit**, einmalig, außerhalb von `Send`. |
| `IniRequest` / `IniResult` | INI senden; Ergebnis trägt Version, Fingerprint (wire + Brief-Format) und den Brief. |
| `HiaRequest` / `HiaResult` | HIA senden; analog, für Auth- und Enc-Schlüssel. |
| `HpbRequest` / `HpbResult` | Bankschlüssel abrufen; optionale erwartete Fingerprints + Trust-Anker (H005). Ergebnis trägt die `BankKeys`. |
| `InitializationLetter` | `{ Text, byte[]? Pdf }` — Ergebnis der Brief-Erzeugung. |
| `EbicsOnboardingException` | Sicherheits-/Integritätsfehler (Hash-Mismatch, Zertifikatsfehler, fehlerhafte Antwort). |

## Versions-Dispatch (H003/H004/H005)

Die Envelope- und PubKeyInfo-Typen sind **pro Version eigene CLR-Klassen** und unterscheiden sich
auf drei Achsen:

| Achse | H003 / H004 (reine Schlüssel) | H005 (zertifikatsbasiert) |
| --- | --- | --- |
| PubKey-Repräsentation | `PubKeyValue`/`RSAKeyValue` (Modulus/Exponent) | nur `X509Data` (Zertifikat) |
| Order-Details | `OrderType` + `OrderAttribute` | `AdminOrderType` |
| INI-OrderData-Namespace | `S001` | `S002` |

Gekapselt wird das in **einem `IOnboardingEnvelopeBuilder` pro Version** hinter einer
`IOnboardingEnvelopeBuilderRegistry` (Muster wie `EbicsVersions`/`KeyVersions`). Der Builder baut die
Requests **und** parst die versionsspezifischen Antworten, sodass die drei Handler
versionsagnostisch bleiben (`IEbicsRequestEnvelope`, `KeyManagementResponseView`, `BankKeys`).
H003/H004 teilen sich die Basis `OnboardingEnvelopeBuilderBase`; H005 nutzt X.509. Für H005 erzeugt
der Handler pro Schlüssel ein kurzlebiges self-signed Zertifikat (`SelfSignedCertificateFactory`).

## Ablauf je Flow (Handler)

- **INI/HIA** (`IniRequestHandler`, `HiaRequestHandler`): Schlüssel aus `IKeyStore` holen →
  OrderData (`SignaturePubKeyOrderData` bzw. `HIARequestOrderData`) bauen → **komprimieren**
  (`EbicsCompression`, ZIP/zlib) → base64 → `ebicsUnsecuredRequest` → serialisieren → Transport →
  Antwort parsen → Returncode → `EbicsResult` (+ Brief).
- **HPB** (`HpbRequestHandler`): `ebicsNoPubKeyDigestsRequest` bauen → serialisieren →
  **X002-Authentifikationssignatur** (`AuthenticationSignature.Sign`) setzen → Transport → Returncode →
  **E002-entschlüsseln** (`EncryptionE002.Decrypt`, privater Teilnehmer-E002-Schlüssel) →
  **dekomprimieren** → `HPBResponseOrderData` parsen → **Fingerprint-Abgleich**
  (`PublicKeyFingerprint.Verify`) gegen den Bankbrief → optional X.509-Kettenprüfung
  (`X509CertificateVerifier`, H005) → Bankschlüssel im `IKeyStore` (`KeyOwner.Bank`) ablegen.

**Fehlergrenze:** fachliche Returncodes → `EbicsResult.Failure` (kein Wurf); technische bzw.
Sicherheitsfehler (Hash-Mismatch, Zertifikatsfehler) → `EbicsOnboardingException`. Bei einem
Mismatch werden die Bankschlüssel **nicht** gespeichert.

## Wiederverwendete Core-Bausteine

`EbicsXmlSerializer` (Serialisierung, neu: `SerializeOrderData`), `EbicsCompression` (neu),
`PublicKeyFingerprint` (Compute/Verify/ToLetterFormat), `AuthenticationSignature` (X002),
`EncryptionE002` (Hybrid), `RsaKeyMaterial` (neu: `Generate`), `RsaKeyImportExport`,
`SelfSignedCertificateFactory` (neu) + `EbicsCertificateProfile` (neu, geteilt mit dem
`X509CertificateVerifier`), `KeyVersions`, `CertificateRequirements`.

## INI-/HIA-Brief (Text + PDF)

`IInitializationLetterRenderer` erzeugt den Brief aus einem reinen `InitializationLetterModel`
(Datum injiziert über `TimeProvider`, daher deterministisch). Der `TextInitializationLetterRenderer`
ist abhängigkeitsfrei; der per `AddEbicoOnboarding()` registrierte `PdfInitializationLetterRenderer`
liefert zusätzlich ein PDF via **QuestPDF** (Community-Lizenz, [ADR-0010](../adr/0010-pdf-bibliothek.md)).
Der Fingerprint erscheint im Brief in der Gruppen-Hex-Darstellung von
`PublicKeyFingerprint.ToLetterFormat` (8 Byte je Zeile).

## Spec-Vorbehalte

In Seams gekapselt und gegen die offiziellen EBICS-Annexe zu verifizieren:
Kompressionsverfahren (zlib vs. raw DEFLATE, `EbicsCompression`); Zuordnung `S001`↔H003/H004 bzw.
`S002`↔H005 der INI-OrderData; ob H005-INI `A005` oder `A006` als Default nimmt; `OrderAttribute`
(`DZNNN`/`DZHNN`) und `SecurityMedium` (`0000`) bei H003/H004; Return-Code-Quelle
(`Body/ReturnCode` primär). Die X.509-KeyUsage-Profile liegen zentral in `EbicsCertificateProfile`.

## Tests

`tests/EBICO.Tests/` — Tier-A (selbst konstruierte Graphen, keine proprietären Samples):
Schlüsselgenerierung, `EbicsCompression`-Round-Trip, `SelfSignedCertificateFactory` (KeyUsage +
Verifier), Brief (Text-Assertions + PDF-Smoke), INI/HIA-Handler **je Version** (Round-Trip: Request
bauen → OrderData dekomprimieren/parsen → eingebetteter Schlüssel = Store-Schlüssel; OK-/Fehler-
Returncode), HPB-Handler (Entschlüsselung + Ablage; **Hash-Mismatch → Exception, nichts gespeichert**;
Fehler-Returncode → `Failure`). Der `FakeTransport` stellt die simulierten Bankantworten.

Seit **#57** laufen INI/HIA/HPB zusätzlich als echter Round-Trip gegen den in-process gehosteten
`EBICO.Server` — inkl. echter Statusmaschine (`New → Initialized → Ready`) und Bankschlüssel-Abruf:
[E2E: Connector ↔ Server](../development/e2e-connector-server.md).
