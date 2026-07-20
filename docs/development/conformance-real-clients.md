# Konformität gegen reale Clients

> Umsetzung von **Issue #59** (Milestone M8 — Validation & Conformance) und Abschluss des M8-Epics
> ([#56](../ticket-overview.md)). Diese Seite beschreibt, wie EBICO gegen **reale, fremde EBICS-Clients**
> geprüft wird — nicht nur gegen die eigene Gegenstelle wie in [#57](e2e-connector-server.md)/[#58](negative-security-cases.md).
>
> Bewusst **enthalten**: ein neuer Testtier `tests/EBICO.Tests/Conformance/` mit committeten Captures eines
> echten Fremd-Clients (`ebics-client`/node-ebics-client, MIT), Parser-/Wire-Shape-Toleranztests, ein
> C14N-Algorithmus-Test, zwei Known-Gap-Negativfälle und eine skip-if-missing-XSD-Validierung. Ergebnis:
> eine **Kompatibilitätsmatrix** und ein **Abweichungs**-Abschnitt.
>
> Bewusst **noch nicht**: eine vollständige Fremd-Client-Palette; Behebung der gefundenen Abweichungen
> (die brauchen die offiziellen XSDs/Annexe, die proprietär und nicht im Repo sind — siehe
> [Lizenz](../legal/ebics-licensing.md)). Die Funde sind hier **dokumentiert**, das Fixen ist Folgearbeit.

## Zweck

Bis M8 war jede EBICO-Seite nur gegen ein **Modell der jeweils anderen** getestet: der Connector gegen
Fake-Bank-Antworten, der Server gegen handgebautes Request-XML, und seit #57 beide gegeneinander. Eine
Wire-Format-Annahme, die **EBICO-Connector und EBICO-Server konsistent teilen, ein realer Fremd-Client
aber nicht**, bleibt in all diesen Aufbauten unsichtbar. Genau diese Klasse schließt #59, indem echte
Fremd-Client-Bytes durch die echte Server-Pipeline laufen.

Und der Aufbau liefert sofort: EBICO nimmt aktuell **keinen** der Onboarding-Requests des realen Clients
an (siehe [Abweichungen](#abweichungen), Punkt 1) — ein Fund, den EBICO↔EBICO-Tests bauartbedingt nicht
finden konnten.

## Test-Ebenen

Alle Tests liegen unter `tests/EBICO.Tests/Conformance/` (xUnit v3 + AwesomeAssertions) und laufen gegen
den in-process gehosteten Server (`WebApplicationFactory<Program>`), wiederverwendet über
[`EbicsE2EHarness`](e2e-connector-server.md).

| Ebene | Datei | Was sie prüft | CI |
| --- | --- | --- | --- |
| **Vendor-Capture** | `VendorCaptureConformanceTests` | Replay echter node-ebics-client-Requests (INI/HIA/HPB) | ✅ (Captures committet) |
| **Wire-Shape-Toleranz** | `OnboardingWireShapeConformanceTests` | Onboarding mit reindentetem / kommentiertem / präfigiertem XML | ✅ |
| **Signierte C14N** | `SignedRequestCanonicalizationConformanceTests` | X002-Verifikation bei inklusiv- **und** exklusiv-C14N | ✅ |
| **Negativ / Known-Gap** | `WireShapeNegativeConformanceTests` | H005-`RSAKeyValue` statt Zertifikat; unkomprimierte Order-Data | ✅ |
| **XSD-Validierung** | `SchemaValidationConformanceTests` | EBICO-Output gegen offizielle XSDs | ⏭️ skip-if-missing (Tier B) |

### Ehrlichkeitsgrenze (was die Tier-A-Ebenen *nicht* beweisen)

- **Wire-Shape-Toleranz** startet von **EBICOs eigenem** Request-XML und formt es um. Das prüft echte
  Parser-Robustheit (Namespace-Präfix statt Default, Whitespace, Kommentare), ist aber **kein** Beleg für
  Konformität gegen einen fremden *Emittenten* — die Nutzlast stammt weiter von EBICO.
- **Signierte C14N** beweist, dass der Server die Kanonisierungs-**Algorithmus-URI aus der Nachricht**
  liest (`C14nAlgorithms.FromAlgorithmUri`), nicht dass EBICOs C14N-Oktette byte-genau mit denen einer
  Fremd-Bibliothek übereinstimmen — das kann nur eine erfasste Fremd-Signatur zeigen.
- Nur die **Vendor-Captures** lösen „gegen echten Client getestet" wirklich ein.

## Kompatibilitätsmatrix

Reale Clients × EBICS-Version × Onboarding-Order, Stand dieses Commits. Legende: ✅ akzeptiert ·
❌ abgelehnt · `–` nicht erfasst.

| Client | Version | INI | HIA | HPB | Status |
| --- | --- | :---: | :---: | :---: | --- |
| [`ebics-client`](https://github.com/node-ebics/node-ebics-client) (node-ebics-client) 5.0.0 | H004 | ❌ | ❌ | ❌ | **Inkompatibel** — `OrderDetails` ohne `xsi:type` (Abweichung 1) |

Weitere Clients/Versionen sind noch nicht erfasst; der Corpus-Loader (`VendorCaptureCorpus`) und die
Verzeichnisstruktur `Conformance/Vendor/<client>/<version>/request/` sind darauf ausgelegt, sie
skip-if-missing zu ergänzen (siehe [Capture-Anleitung](#capture-anleitung)).

> Der **EBICO-Connector selbst** deckt H003/H004/H005 vollständig ab ([#57](e2e-connector-server.md)),
> zählt hier aber nicht als „realer Fremd-Client" — er teilt EBICOs Wire-Annahmen.

## Abweichungen

### 1. `OrderDetails` erfordert einen `xsi:type` (kritisch, blockiert reale Clients)

EBICOs generierte H004-Bindings typisieren das `OrderDetails`-Element (im Static-Header von
`ebicsUnsecuredRequest` **und** `ebicsNoPubKeyDigestsRequest`) als den **abstrakten** Basistyp
`OrderDetailsType`. Der `XmlSerializer` braucht dann einen `xsi:type`-Diskriminator, um den konkreten
Typ (`UnsecuredReqOrderDetailsType` / `NoPubKeyDigestsReqOrderDetailsType`) zu wählen — und EBICOs
**eigener** Connector emittiert diesen Diskriminator, weshalb EBICO↔EBICO funktioniert.

node-ebics-client (wie reale Clients allgemein, die dem konkreten Schematyp folgen) emittiert
`<OrderDetails>` **ohne** `xsi:type`. Der Server kann den Request dann nicht deserialisieren:

```
System.InvalidOperationException: The specified type is abstract:
  name='OrderDetailsType', namespace='urn:org:ebics:H004', at <OrderDetails>.
```

Folge: **alle drei** Onboarding-Requests (INI/HIA/HPB) werden abgelehnt. Zwei Teilprobleme:

1. **Binding-Striktheit.** Vermutlich sollte `OrderDetails` in den Unsecured-/NoPubKeyDigests-Headern
   **konkret** typisiert sein (dann emittiert *und* akzeptiert EBICO ohne `xsi:type`). Das ist eine
   Änderung an den generierten Bindings bzw. deren Erzeugung und muss gegen die offiziellen XSDs
   verifiziert werden (nicht im Repo → Folgearbeit, kein Fix in #59).
2. **Fehlklassifikation.** Ein vom Client stammendes, nicht deserialisierbares XML wird aktuell als
   `061099 EBICS_INTERNAL_ERROR` (Server-Fehler) beantwortet statt als Client-Fehler (z. B.
   `091010 EBICS_INVALID_XML`). Der zentrale `EbicsErrorMapper` fängt nur
   `InvalidOperationException { InnerException: XmlException }` als `InvalidXml` ab; die
   XmlSerializer-Typ-Ausnahme hat einen anderen Inner-Typ und fällt auf `InternalError` durch.

Charakterisiert durch `VendorCaptureConformanceTests` (erwartet aktuell `061099`), damit ein Fix
beider Hälften diesen Test bricht und diese Doku nachgezogen wird.

### 2. `A006` (RSASSA-PSS) auf H004 (sekundär, aktuell verdeckt)

node-ebics-client signiert seine INI-Order-Data mit **`A006`** (`SignatureVersion`). EBICO erlaubt `A006`
nur für **H005** (`KeyVersions`, PSS erst mit EBICS 3.0). Selbst wenn Abweichung 1 behoben wäre, würde die
H004-INI daran mit `090004` scheitern. Ob `A006` auf H004 spec-konform ist, muss gegen die offiziellen
Annexe geklärt werden (proprietär, nicht im Repo). Aktuell **verdeckt**, weil die Deserialisierung (1)
vorher scheitert.

### 3. Fortbestehende Spec-Vorbehalte (konsolidiert)

Aus [#57](e2e-connector-server.md)/[#58](negative-security-cases.md) und der
[Order-Abdeckungsmatrix](../server/order-coverage-matrix.md), hier gebündelt:

- **Server-Antworten sind unsigniert** (der Connector prüft keine Antwortsignatur).
- **ES/A00x-Ordersignatur** wird serverseitig nicht verifiziert.
- **camt fest auf `.001.08`**; keine echte ISO-20022-XSD-Validierung.
- **HAC/PTK** als Eigen-Projektion statt spec-genauem camt.086/pain.002; **HVT** auftrags-summarisch.
- **BTF-Katalog** ist Best-Effort gegen die proprietäre External Code List.
- **C14N** von X002 ist nicht byte-genau gegen die offiziellen Annexe verifiziert.

## Capture-Anleitung

Die Captures werden **einmalig, lokal, offline** mit dem Werkzeug unter [`tools/vendor-capture/`](../../tools/vendor-capture/README.md)
erzeugt:

```bash
cd tools/vendor-capture
npm install        # ebics-client (MIT) — nur lokal, nie in der CI
node capture.js
```

Der Client postet gegen eine lokale Wegwerf-Senke (nie eine echte Bank); nur der Request wird abgegriffen
und nach `tests/EBICO.Tests/Conformance/Vendor/node-ebics-client/H004/request/{ini,hia,hpb}.xml`
geschrieben. Details/Lizenz/Wegwerf-Keys: die
[`PROVENANCE.md`](../../tests/EBICO.Tests/Conformance/Vendor/node-ebics-client/PROVENANCE.md) im Corpus.

**Einen weiteren Client ergänzen:** dessen Requests unter
`Conformance/Vendor/<client>/<version>/request/*.xml` ablegen (Pfad ist **nicht** `.gitignore`d, siehe
[ADR-0026](../adr/0026-konformitaet-gegen-reale-clients.md)), eine `PROVENANCE.md` beilegen und einen
Replay in `VendorCaptureConformanceTests` mit den erwarteten Returncodes ergänzen. Fehlt der Corpus,
skippen die Replays — die CI bleibt grün.

> **Nicht committen:** offizielle ebics.org-Beispiel-XML und XSDs bleiben proprietär und `.gitignore`d
> (`tests/**/Fixtures/Xml/**/*.xml`, `schemas/**/*.xsd`). Der Vendor-Corpus ist ausdrücklich etwas
> anderes: Output eines permissiv (MIT/Apache) lizenzierten OSS-Clients.

## Tests

- `tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs` — Replay der committeten
  node-ebics-client-Captures (charakterisiert Abweichung 1).
- `tests/EBICO.Tests/Conformance/OnboardingWireShapeConformanceTests.cs` — Parser-/Wire-Shape-Toleranz
  (`XmlShape`: Reindent, Kommentare, Namespace-Präfix) über H003/H004/H005.
- `tests/EBICO.Tests/Conformance/SignedRequestCanonicalizationConformanceTests.cs` — inklusiv/exklusiv
  C14N gegen den X002-Verifier.
- `tests/EBICO.Tests/Conformance/WireShapeNegativeConformanceTests.cs` — H005-`RSAKeyValue`-Gap und
  unkomprimierte Order-Data (je `090004`).
- `tests/EBICO.Tests/Conformance/SchemaValidationConformanceTests.cs` — XSD-Validierung (Tier B,
  skip-if-missing).
- `tests/EBICO.Tests/Docs/ConformanceMatrixTests.cs` — Doku-Guard (hält diese Seite mit den Pflicht-
  Abschnitten synchron).

## Verwandte Doku

- [E2E: Connector ↔ Server (Happy Paths)](e2e-connector-server.md) — die Basis-Harness (#57)
- [E2E: Negativ- & Sicherheitsfälle](negative-security-cases.md) — X002-Verifikation, Tampering (#58)
- [Test-Harness & Fixtures](testing.md) — Tier-A/B, `Conformance/`, `SampleXml`, `CanonicalXmlComparer`
- [Order-/BTF-Abdeckungsmatrix](../server/order-coverage-matrix.md) — Order × Version × Status
- [ADR-0026 — Konformität gegen reale Clients](../adr/0026-konformitaet-gegen-reale-clients.md)
- [Lizenz & Repo-Policy](../legal/ebics-licensing.md) — proprietäre Schemas/Beispiele vs. OSS-Output
