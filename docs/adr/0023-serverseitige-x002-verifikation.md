# 0023 — Serverseitige X002-Authentifikationssignatur-Verifikation

- Status: accepted
- Datum: 2026-07-17

## Kontext

Bis Milestone M8 war die *Verify*-Stufe der Server-Pipeline ein No-Op
(`NoOpEbicsRequestVerifier`): der Connector signierte jeden `ebicsRequest` mit X002, der Server
prüfte die Signatur aber nicht. Damit war die Authentifikationssignatur in **keiner** Richtung
getestet — eine Lücke, die Issue **#58** (Negativ-/Sicherheitsfälle) schließen soll. Die Krypto-
Primitive [`AuthenticationSignature.Verify`](../protocol/auth-signature-x002.md) und die
Envelope-Abstraktion `IAuthSignedRequestEnvelope` existierten bereits; es fehlte die Verdrahtung im
Server (Subscriber-Auflösung + Key-Lookup + Fehlercode-Pfad).

Erschwerend: Transfer-/Receipt-Requests tragen nur die HostID im Header (Teilnehmer an die
Transaktion gebunden), und vor HIA existiert serverseitig gar kein Auth-Key, gegen den geprüft
werden könnte. Eine naive „immer strikt prüfen"-Variante hätte viele bestehende Server-Tests
(hand-gebautes, unsigniertes XML) sowie den Onboarding-Bootstrap gebrochen.

## Entscheidung

Ein produktiver `X002EbicsRequestVerifier` ersetzt den No-Op als Default (`AddEbicoServer`,
weiterhin per `TryAddSingleton` austauschbar). Sein Verhalten:

- **Nur signierte `ebicsRequest`** werden geprüft (Upload-Init/-Transfer, Download-Init/-Transfer/
  -Receipt, HCA/HCS/SPR). `ebicsUnsecuredRequest` (INI/HIA/HSA) und `ebicsNoPubKeyDigestsRequest`
  (HPB) werden übersprungen.
- **Subscriber-Auflösung:** aus dem Static-Header-Tripel (Init/Einphasen) bzw. über den
  Upload-/Download-Transaction-Store (Transfer/Receipt, nur HostID im Header).
- **Verifikation nur bei vorhandenem Auth-Key** (nach HIA). Ohne Key: `Success` — der
  Zustandsautomat lehnt verfrühte Aufträge mit `091002` ab. Mit Key ist eine gültige `AuthSignature`
  Pflicht; Fehlen/Fehlschlag → `EBICS_AUTHENTICATION_FAILED` (`061001`, technisch → Header).

Getestet end-to-end durch die Negativsuite (`NegativeSecurityE2ETests`, Wire-Tampering) und implizit
durch die unveränderten Happy-Path-E2E, die nun eine echte Connector-Signatur serverseitig
verifizieren. Siehe [Negativ-/Sicherheitsfälle](../development/negative-security-cases.md).

## Konsequenzen

- Der Server lehnt manipulierte/falsch signierte `ebicsRequest` standardmäßig mit `061001` ab; der
  gesamte `authenticate="true"`-Header (inkl. Segment-Metadaten) ist damit geschützt.
- **Blast-Radius auf bestehende Tests minimal:** nur die zwei HCA/HCS-Happy-Path-Tests, die einen
  Auth-Key seeden **und** einen unsignierten Request schickten, mussten angepasst werden — sie
  signieren jetzt via `ServerTestHelpers.SignRequestXml` mit dem hinterlegten Auth-Key. Alle übrigen
  Server-Tests seeden keinen Auth-Key und fallen daher in den `Success`-Zweig.
- Die Verifikation ist **bewusst bedingt** (nur bei vorhandenem Key): das modelliert den
  EBICS-Bootstrap (vor HIA keine Prüfung möglich) und hält die Onboarding-Flows und Wire-Level-
  Server-Tests funktionsfähig. Ein Angreifer, der als nicht-onboardeter Teilnehmer signiert, scheitert
  ohnehin am Zustandsautomaten (`091002`).

## Alternativen

- **No-Op belassen (Status quo):** verworfen — die Signatur bliebe ungetestet, #58 nicht erfüllbar.
- **Immer strikt prüfen (Signatur auf jedem `ebicsRequest` verpflichtend, unabhängig vom Key-Status):**
  verworfen — bricht Onboarding-Bootstrap und die unsignierten Wire-Level-Server-Tests, ohne
  Sicherheitsgewinn (der Zustandsautomat fängt keylose Teilnehmer bereits ab).
- **Verifikation nur an der Initialisation-Phase (Transfer/Receipt ungeprüft):** als Fallback erwogen;
  verworfen, weil der Header auch dort signiert ist und die Transaction-Store-Auflösung günstig
  verfügbar war — vollständige Prüfung ist die kohärentere Wahl.
