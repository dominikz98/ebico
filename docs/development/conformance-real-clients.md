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
> Bewusst **noch nicht**: eine vollständige Fremd-Client-Palette.
>
> **Nachtrag [#117](#behobene-abweichungen-117):** die von #59 gefundenen Abweichungen sind **behoben**
> ([ADR-0029](../adr/0029-interop-fixes-reale-clients.md)). Der Vendor-Replay ist damit kein
> Charakterisierungstest eines Defekts mehr, sondern der **positive Konformitätsnachweis**: die
> committeten Fremd-Client-Bytes treiben die volle Onboarding-Kette INI → HIA → HPB durch.

## Zweck

Bis M8 war jede EBICO-Seite nur gegen ein **Modell der jeweils anderen** getestet: der Connector gegen
Fake-Bank-Antworten, der Server gegen handgebautes Request-XML, und seit #57 beide gegeneinander. Eine
Wire-Format-Annahme, die **EBICO-Connector und EBICO-Server konsistent teilen, ein realer Fremd-Client
aber nicht**, bleibt in all diesen Aufbauten unsichtbar. Genau diese Klasse schließt #59, indem echte
Fremd-Client-Bytes durch die echte Server-Pipeline laufen.

Und der Aufbau lieferte sofort: EBICO nahm **keinen** der Onboarding-Requests des realen Clients an —
ein Fund, den EBICO↔EBICO-Tests bauartbedingt nicht machen konnten. Dahinter lagen weitere Defekte, die
jeweils erst nach dem Fix davor sichtbar wurden. Alle sind mit #117 behoben; die Analyse steht in
[Behobene Abweichungen](#behobene-abweichungen-117).

## Test-Ebenen

Alle Tests liegen unter `tests/EBICO.Tests/Conformance/` (xUnit v3 + AwesomeAssertions) und laufen gegen
den in-process gehosteten Server (`WebApplicationFactory<Program>`), wiederverwendet über
[`EbicsE2EHarness`](e2e-connector-server.md).

| Ebene | Datei | Was sie prüft | CI |
| --- | --- | --- | --- |
| **Vendor-Capture** | `VendorCaptureConformanceTests` | Sequenzieller Replay echter node-ebics-client-Requests (INI → HIA → HPB) bis `SubscriberState.Ready` | ✅ (Captures committet) |
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
- Auch der Vendor-Replay prüft **keine Fremd-Signatur**: das HPB-Capture trägt zwar eine echte
  X002-`AuthSignature` von node-ebics-client, aber `X002EbicsRequestVerifier` überspringt
  `ebicsNoPubKeyDigestsRequest` (der Request bootstrappt den Schlüsselaustausch, ADR-0023). EBICOs
  C14N-Oktette sind damit weiterhin **nicht** gegen einen fremden Signierer verifiziert. Erst ein
  Capture eines *signierten* `ebicsRequest` (nach abgeschlossenem Onboarding) würde das einlösen.

## Kompatibilitätsmatrix

Reale Clients × EBICS-Version × Onboarding-Order, Stand dieses Commits. Legende: ✅ akzeptiert ·
❌ abgelehnt · `–` nicht erfasst.

| Client | Version | INI | HIA | HPB | Status |
| --- | --- | :---: | :---: | :---: | --- |
| [`ebics-client`](https://github.com/node-ebics/node-ebics-client) (node-ebics-client) 5.0.0 | H004 | ✅ | ✅ | ✅ | **Kompatibel** seit [#117](#behobene-abweichungen-117) — die Kette treibt den Teilnehmer bis `Ready` |

Weitere Clients/Versionen sind noch nicht erfasst; der Corpus-Loader (`VendorCaptureCorpus`) und die
Verzeichnisstruktur `Conformance/Vendor/<client>/<version>/request/` sind darauf ausgelegt, sie
skip-if-missing zu ergänzen (siehe [Capture-Anleitung](#capture-anleitung)).

> Der **EBICO-Connector selbst** deckt H003/H004/H005 vollständig ab ([#57](e2e-connector-server.md)),
> zählt hier aber nicht als „realer Fremd-Client" — er teilt EBICOs Wire-Annahmen.

## Abweichungen

### Behobene Abweichungen (#117)

Die Funde lagen **hintereinander auf demselben Pfad**: jeder verdeckte den nächsten, deshalb kannte #59
nur den ersten (und dessen Fehlklassifikation). Entscheidungen und verworfene Alternativen:
[ADR-0029](../adr/0029-interop-fixes-reale-clients.md).

#### 1. `OrderDetails` erforderte einen `xsi:type` (kritisch, blockierte reale Clients)

EBICOs generierte Bindings typisierten das `OrderDetails`-Element (im Static-Header von
`ebicsUnsecuredRequest` **und** `ebicsNoPubKeyDigestsRequest`) als den **abstrakten** Basistyp
`OrderDetailsType` — `xscgen` übersetzt die XSD-`<restriction>`, die das Element konkreter typisiert,
nicht. Der `XmlSerializer` braucht dann einen `xsi:type`-Diskriminator, den EBICOs **eigener** Connector
emittierte (deshalb war EBICO↔EBICO grün), ein realer Client aber weglässt:

```
System.InvalidOperationException: The specified type is abstract:
  name='OrderDetailsType', namespace='urn:org:ebics:H004', at <OrderDetails>.
```

Folge: **alle drei** Onboarding-Requests wurden abgelehnt.

**Fix:** `OrderDetailsType` ist in allen drei Versionen **konkret**; die `[XmlInclude]`-Attribute bleiben,
`xsi:type` wird also weiter *akzeptiert*, aber nicht mehr *verlangt*. Der Connector emittiert den
Basistyp und damit gar keinen Diskriminator mehr. Weil das ein Eingriff in generierten Code ist, wendet
`scripts/generate-bindings.sh` ihn per `apply_binding_fixups()` nach jedem Lauf erneut an und bricht ab,
wenn das Muster fehlt; `OrderDetailsBindingTests` hält beide Richtungen fest. Siehe
[XSD-Bindings → Manuelle Fixups](../protocol/xsd-bindings.md#manuelle-fixups-nach-der-generierung).

#### 2. Fehlklassifikation: `061099` statt `091010`

Nicht abbildbares Client-XML wurde als `061099 EBICS_INTERNAL_ERROR` beantwortet — EBICO gab **sich
selbst** die Schuld an einem fremden Dokument. Der `EbicsErrorMapper` fing nur
`InvalidOperationException { InnerException: XmlException }`; die XmlSerializer-Typausnahme trägt einen
anderen Inner-Typ und fiel auf `InternalError` durch.

**Fix:** `EbicsXmlSerializer.DeserializeEnvelope` übersetzt Abbildungsfehler des `XmlSerializer` in
`EbicsEnvelopeFormatException` → `091010 EBICS_INVALID_XML`. Bewusst an der Envelope-Grenze, nicht im
Error-Mapper: nur dort ist bekannt, dass die Bytes vom Client stammen. Der Order-Data-Pfad bleibt
unberührt (`OrderDataFault` → `090004`).

#### 3. `A006` (RSASSA-PSS) auf H004

node-ebics-client signiert seine INI-Order-Data mit **`A006`** (`SignatureVersion`); EBICO erlaubte `A006`
nur für **H005**, die H004-INI scheiterte daran mit `090004`.

**Fix:** `KeyVersions` erlaubt `A006` für **H004 und H005**; H003 (EBICS 2.4) bleibt ausgeschlossen.
⚠️ **Spec-Vorbehalt:** die Evidenz ist ein realer Client plus die verbreitete Lesart (EBICS 2.5 Annex 1
kennt A005 **und** A006) — gegen die offiziellen Annexe (proprietär, nicht im Repo) ist das **nicht**
verifiziert.

#### 4. `ds:Modulus` mit ASN.1-Vorzeichen-Byte (erst nach 1.–3. sichtbar)

`ds:Modulus` ist per XML-DSig ein `CryptoBinary` ohne führende Null; reale Clients senden bei gesetztem
höchsten Bit trotzdem die 257-Byte-INTEGER-Form (`AM/PbALU…`). `RsaKeyMaterial` normalisierte zwar die
nach außen sichtbaren Bytes (Fingerprint, `KeySizeBits` = 2048), importierte aber die **rohen** Parameter
— das ergab eine 2056-Bit-RSA-Instanz, deren OAEP-Operationen scheiterten. HPB konnte die Bankschlüssel
deshalb nicht für den Teilnehmer verschlüsseln und antwortete mit `090004`.

**Fix:** `RsaKeyMaterial` importiert aus der **kanonischen** Form; die drei Sichten auf denselben
Schlüssel (exponierte Bytes, `KeySizeBits`, importierte RSA-Instanz) stimmen wieder überein.

### Fortbestehende Spec-Vorbehalte (konsolidiert)

Aus [#57](e2e-connector-server.md)/[#58](negative-security-cases.md) und der
[Order-Abdeckungsmatrix](../server/order-coverage-matrix.md), hier gebündelt:

- **Server-Antworten sind unsigniert** (der Connector prüft keine Antwortsignatur).
- **ES/A00x-Ordersignatur** wird serverseitig nicht verifiziert.
- **camt fest auf `.001.08`**; keine echte ISO-20022-XSD-Validierung.
- **HAC/PTK** als Eigen-Projektion statt spec-genauem camt.086/pain.002; **HVT** auftrags-summarisch.
- **BTF-Katalog** ist Best-Effort gegen die proprietäre External Code List.
- **C14N** von X002 ist nicht byte-genau gegen die offiziellen Annexe verifiziert — und auch nicht gegen
  einen fremden Signierer (der Vendor-Replay umfasst nur unsignierte bzw. verifikationsfreie Requests).
- **Binding-Konkretisierung von `OrderDetails`** und **`A006` auf H004** sind gegen einen realen Client
  belegt, aber **nicht** gegen die offiziellen XSDs/Annexe (ADR-0029).

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
Replay in `VendorCaptureConformanceTests` mit den erwarteten Returncodes ergänzen. Die im Capture
verwendeten `HostID`/`PartnerID`/`UserID` müssen dabei als Stammdaten geseedet werden — der Replay
onboardet einen realen Teilnehmer, er stubbt ihn nicht. Fehlt der Corpus, skippen die Replays — die CI
bleibt grün.

> **Nicht committen:** offizielle ebics.org-Beispiel-XML und XSDs bleiben proprietär und `.gitignore`d
> (`tests/**/Fixtures/Xml/**/*.xml`, `schemas/**/*.xsd`). Der Vendor-Corpus ist ausdrücklich etwas
> anderes: Output eines permissiv (MIT/Apache) lizenzierten OSS-Clients.

## Tests

- `tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs` — sequenzieller Replay der committeten
  node-ebics-client-Captures (INI → HIA → HPB bis `SubscriberState.Ready`).
- `tests/EBICO.Tests/Serialization/OrderDetailsBindingTests.cs` — Guard für den Binding-Fixup:
  `OrderDetailsType` nicht abstrakt, Empfang mit **und** ohne `xsi:type`, Ausgabe ohne (#117).
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
- [ADR-0029 — Interop-Fixes für reale Clients](../adr/0029-interop-fixes-reale-clients.md) — die
  Behebung der hier gefundenen Abweichungen (#117)
- [XSD-Bindings](../protocol/xsd-bindings.md) — der Fixup-Schritt am Generator
- [Lizenz & Repo-Policy](../legal/ebics-licensing.md) — proprietäre Schemas/Beispiele vs. OSS-Output
