# 0026 — Konformität gegen reale Clients (Vendor-Captures, Test-Ebenen, Abweichungs-Politik)

- Status: accepted
- Datum: 2026-07-20

## Kontext

Milestone M8 verlangt in Issue **#59** den Nachweis der Konformität **gegen reale, fremde EBICS-Clients**
— nicht nur gegen die eigene Gegenstelle (#57/#58). Zwei harte Randbedingungen kollidieren dabei:

1. Die **CI läuft offline** und kann keinen Java-/Node-/Python-Fremd-Client ausführen.
2. Die offiziellen EBICS-**XSDs und Beispiel-XML sind proprietär** (EBICS SC) und werden nicht committet
   (`.gitignore`: `schemas/**/*.xsd`, `tests/**/Fixtures/Xml/**/*.xml`, siehe
   [ADR-0003](0003-umgang-mit-proprietaeren-schemas.md)).

Eine reine „skip-if-missing"-Lösung (wie bei den proprietären Beispielen) liefe in der CI nie und würde
den Kern von #59 — der Beleg gegen *echte* Fremd-Bytes — nie erbringen.

## Entscheidung

Ein neuer Testtier `tests/EBICO.Tests/Conformance/` mit mehreren Ebenen, dazu eine klare Politik für
Captures und für den Umgang mit gefundenen Abweichungen.

**1. Committete Vendor-Captures als tragende Ebene.** Der *Output* eines permissiv lizenzierten
OSS-Clients (MIT/Apache) ist **weder Eigentum der EBICS SC noch ein Derivat einer proprietären
XSD/Beispieldatei** — es sind mit Wegwerf-Schlüsseln erzeugte Daten. Solche Captures **dürfen committet**
werden, unter dem **nicht** ge-`.gitignore`-ten Pfad
`tests/EBICO.Tests/Conformance/Vendor/<client>/<version>/request/*.xml` (+ `PROVENANCE.md`). Sie laufen
damit **permanent in der CI**. Konkret erfasst: `ebics-client` (node-ebics-client, MIT, H004), erzeugt
mit dem einmaligen, lokalen Werkzeug `tools/vendor-capture/` (nicht Teil von Build/CI). Offizielle
ebics.org-Beispiele bleiben davon getrennt und weiterhin skip-if-missing.

**2. Wire-Shape-Toleranz bewusst als Parser-Proxy.** Zusätzliche Tier-A-Tests formen EBICOs *eigenes*
Request-XML in legitime Fremd-Formen um (Namespace-Präfix statt Default inkl. `xsi:type`-Umschreibung,
Whitespace, Kommentare). Sie sind CI-grün und prüfen echte Parser-Robustheit, sind aber **kein** Beleg
gegen einen fremden Emittenten — diese Ehrlichkeitsgrenze ist dokumentiert.

**3. Abweichungen dokumentieren statt Protokoll fixen.** #59 **findet und dokumentiert** Abweichungen;
es **ändert nicht** das Protokoll-/Binding-Verhalten. Änderungen an den generierten Bindings oder an
Krypto-/Serialisierungsdetails erfordern die offiziellen XSDs/Annexe (proprietär, nicht im Repo) und
folgen dem Grundsatz „Evidence > assumptions". Gefundene Abweichungen werden charakterisiert (Tests, die
das *aktuelle* Verhalten festhalten) und in
[docs/development/conformance-real-clients.md](../development/conformance-real-clients.md) samt
Folgearbeit beschrieben.

## Konsequenzen

- Der Vendor-Replay findet sofort die **wichtigste Interop-Abweichung**, die EBICO↔EBICO-Tests
  bauartbedingt nicht sehen können: EBICOs generiertes H004-Binding typisiert `OrderDetails` **abstrakt**
  und verlangt einen `xsi:type`-Diskriminator, den EBICOs eigener Connector emittiert, ein realer Client
  (node-ebics-client) aber weglässt. Folge: **alle** Onboarding-Requests des realen Clients werden
  abgelehnt (damals als `061099`, also fälschlich als Server- statt Client-Fehler). Das war als
  Charakterisierungstest festgehalten und als Folgearbeit benannt (Binding konkret typisieren; und
  nicht-deserialisierbares Client-XML auf einen Client-Fehlercode mappen) — **erledigt in
  [ADR-0029](0029-interop-fixes-reale-clients.md) / Issue #117**, zusammen mit zwei weiteren Defekten,
  die erst dahinter sichtbar wurden (`A006` auf H004, Modulus mit ASN.1-Vorzeichen-Byte).
- Der Corpus ist **erweiterbar**: weitere Clients/Versionen werden per Verzeichnis + `PROVENANCE.md` +
  Replay ergänzt; fehlt der Corpus, skippen die Replays und die CI bleibt grün.
- Der Doku-Guard `ConformanceMatrixTests` hält die Kompatibilitätsmatrix-Seite mit ihren Pflicht-
  Abschnitten synchron (Muster wie `OrderCoverageMatrixTests`).
- Der M8-Epic ([#56](../ticket-overview.md)) ist mit #59 abgeschlossen; die drei Sub-Issues (#57/#58/#59)
  sind erledigt.

## Alternativen

- **Nur skip-if-missing (proprietäre Beispiele lokal ablegen):** verworfen — läuft nie in der CI, erfüllt
  den Kern von #59 (echte Fremd-Bytes) nicht.
- **Fremd-Client live in der CI ausführen (Node/Java):** verworfen — die CI ist offline; ein
  Cross-Runtime-Handshake im Build ist fragil und netzabhängig. Die Capture-Erzeugung bleibt einmalig
  und lokal, die CI *replayt* nur.
- **Die gefundene `OrderDetails`/`xsi:type`-Abweichung sofort im Binding fixen:** verworfen für #59 —
  betrifft generierte Bindings und EBICOs eigenes Wire-Format (breiter Blast-Radius) und ist ohne die
  offiziellen XSDs nicht verifizierbar. Gehört als eigene, spec-gestützte Folgearbeit dokumentiert.
  → Nachgeholt in **[ADR-0029](0029-interop-fixes-reale-clients.md)** (Issue #117): Binding konkret,
  Fehlklassifikation behoben, `A006` auf H004, Modulus-Normalisierung — der Vendor-Replay ist damit
  vom Charakterisierungs- zum Konformitätstest geworden.
- **Nur EBICO-eigenes XML umformen (keine Vendor-Captures):** verworfen — beweist nur Parser-Toleranz,
  nicht Konformität gegen einen fremden Emittenten.
