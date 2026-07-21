# Vendor-Captures: node-ebics-client

Echte EBICS-Request-XML, erzeugt von einem **Fremd-Client**, für EBICOs Konformitäts-Replay
(`tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs`, Issue #59).

| Feld | Wert |
| --- | --- |
| Client | [`ebics-client`](https://github.com/node-ebics/node-ebics-client) (npm) |
| Version | 5.0.0 |
| Lizenz | **MIT** |
| EBICS-Wire-Version | H004 |
| Erzeugt mit | `tools/vendor-capture/` (siehe dessen README) |

## Inhalt

`H004/request/{ini,hia,hpb}.xml` — die drei Onboarding-Requests (INI, HIA, HPB), abgegriffen an einer
lokalen Wegwerf-Senke (nie an einer echten Bank).

## Warum das committet werden darf

Diese Dateien sind der **Output des OSS-Clients**, kein Eigentum der EBICS SC und kein Derivat einer
proprietären XSD/Beispieldatei — anders als die offiziellen ebics.org-Beispiel-XML (die bleiben
`.gitignore`d). Begründung: [ADR-0026](../../../../../docs/adr/0026-konformitaet-gegen-reale-clients.md).

## Sicherheit

**Alles Schlüsselmaterial ist Wegwerf-Material**, einmalig lokal erzeugt (RSA-Schlüssel in den
Order-Daten, X002-Signatur in HPB, Nonces/Timestamps). Es gehört zu keinem echten Teilnehmer und keiner
echten Bank. Die IDs (`EBICOHOST`/`PARTNER1`/`USER1`) sind Platzhalter.

Neu erzeugen: `cd tools/vendor-capture && npm install && node capture.js` (erzeugt frische Wegwerf-Keys).
