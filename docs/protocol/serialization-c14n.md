# XML-Serialisierung & Canonicalization (C14N)

Wie `EBICO.Core` EBICS-Envelopes **deterministisch** serialisiert und für Signaturen
**kanonisiert** (C14N). Setzt auf die committeten [XSD-Bindings](xsd-bindings.md)
(#11–#13) und den [Versions-Dispatch](version-dispatch.md) (#14) auf. Issue **#15**
(Milestone M1).

## Bausteine

Alle unter `src/EBICO.Core/Serialization/`:

| Baustein | Ort | Aufgabe |
|---|---|---|
| `EbicsXmlSerializer` | `Serialization/EbicsXmlSerializer.cs` | deterministisches Serialisieren + versions­erkennendes Deserialisieren von Envelopes |
| `XmlCanonicalizer` | `Serialization/XmlCanonicalizer.cs` | Kanonform (C14N) als UTF-8-Oktette; inklusiv **und** exklusiv |
| `C14nMode` / `C14nAlgorithms` | `Serialization/C14nAlgorithm.cs` | die vier C14N-Varianten + Mapping zur `ds:CanonicalizationMethod/@Algorithm`-URI |

## Deterministische Serialisierung

Damit dieselbe Objektstruktur stets dieselben Bytes ergibt (und über H003/H004/H005
strukturgleich bleibt), legt `EbicsXmlSerializer` fest:

- **UTF-8 ohne BOM**, mit XML-Deklaration, ohne Einrückung
  (`encoding="utf-8"` korrekt in der Deklaration — die Serialisierung läuft über einen
  `MemoryStream`, nicht über einen `StringWriter`, der `utf-16` deklarieren würde).
- **Stabile Prefix-Map** je Version via `XmlSerializerNamespaces`: der Protokoll-Namespace
  als **Default** (Wurzel unpräfigiert), `ds` für XML-DSig. Das unterdrückt zugleich das
  automatische `xmlns:xsi`/`xmlns:xsd`-Rauschen.
- Die **Element-/Attribut-Reihenfolge** ist bereits durch die generierten Bindings fest;
  der Serializer fügt nur Kodierung, Namespaces und Formatierung deterministisch hinzu.
- `XmlSerializer`-Instanzen werden je Typ **gecacht** (Konstruktion ist teuer).

```csharp
var request = new EBICO.Core.Schema.H005.EbicsRequest { Version = "H005" };

byte[] bytes  = EbicsXmlSerializer.SerializeToUtf8Bytes(request); // Wire-Bytes
string xml    = EbicsXmlSerializer.SerializeToString(request);
//            → <?xml version="1.0" encoding="utf-8"?>
//              <ebicsRequest xmlns="urn:org:ebics:H005" xmlns:ds="…" Version="H005" />
```

Symmetrisch dazu **erkennt** `DeserializeEnvelope` die Version selbst: der Wurzel-Namespace
wählt über den [`EbicsVersionDetector`](version-dispatch.md) die Version (inkl.
H003-Legacy-Sonderfall), der Wurzel-**Elementname** eines der sechs Envelopes
(`ebicsRequest` → `RequestType`, `ebicsResponse` → `ResponseType`, …
`ebicsKeyManagementResponse` → `KeyManagementResponseType`):

```csharp
IEbicsEnvelope envelope = EbicsXmlSerializer.DeserializeEnvelope(rawXml);
// envelope.ProtocolVersion → die erkannte Version; konkreter Typ je nach Wurzelelement
```

Eingehendes XML wird **gegen DTD/XXE gehärtet** (`DtdProcessing.Prohibit`,
`XmlResolver = null`) — ein `<!DOCTYPE …>` wird abgelehnt. Unbekannte Wurzelelemente in
einem bekannten Namespace ergeben eine `EbicsEnvelopeFormatException`, ein unbekannter
Namespace eine `EbicsVersionNotSupportedException`.

## Canonicalization (C14N)

`XmlCanonicalizer` liefert die **Kanonform als UTF-8-`byte[]`** — genau die Bytes, über die
eine EBICS-Authentifizierungssignatur ihren Digest bildet. Unterstützt werden beide
Familien, gewählt über `C14nMode`:

| `C14nMode` | Algorithmus-URI |
|---|---|
| `Inclusive` *(Default)* | `http://www.w3.org/TR/2001/REC-xml-c14n-20010315` |
| `InclusiveWithComments` | …`#WithComments` |
| `Exclusive` | `http://www.w3.org/2001/10/xml-exc-c14n#` |
| `ExclusiveWithComments` | …`#WithComments` |

```csharp
byte[] c14n = XmlCanonicalizer.Canonicalize(xml);                       // inklusiv (Default)
byte[] exc  = XmlCanonicalizer.Canonicalize(xml, C14nMode.Exclusive);   // exklusiv
```

- **Whitespace-treu:** geladen wird mit `PreserveWhitespace = true` — anders als der
  whitespace-tolerante Test-Helfer `CanonicalXmlComparer` (der für Vergleichszwecke
  belanglose Formatierung verwirft), denn die Kanon-Oktette sind das **signierte Material**.
- **Gleiche Härtung** wie oben (DTD/XXE).
- `C14nAlgorithms.FromAlgorithmUri` / `ToAlgorithmUri` bilden die URI auf den Modus ab und
  zurück — so kann der Signaturcode (M2) die Methode aus einem `SignedInfo` ableiten.
- Der `inclusiveNamespacePrefixList`-Parameter wirkt nur in den exklusiven Modi
  (`InclusiveNamespaces`-PrefixList).

> **Inklusiv vs. exklusiv — Kernunterschied:** Eine auf einem Vorfahren deklarierte, im
> Teilbaum **ungenutzte** Namespace-Deklaration behält die *inklusive* C14N bei, die
> *exklusive* lässt sie weg. Genau das prüft der Differenzierer-Testvektor.

> ⚠️ **Spec-Vorbehalt (Default = inklusiv).** Der Issue-Text nennt „exklusive C14N", die
> EBICS-Authentifizierungssignatur verwendet aber sehr wahrscheinlich **inklusive**
> Canonical XML 1.0. Die offiziellen XSDs/Annexe liegen nicht im Repo (vgl.
> [Schema-Quellen](schema-sources.md) und [ADR-0003](../adr/0003-umgang-mit-proprietaeren-schemas.md)),
> daher ist die Primitive bewusst für **beide** Algorithmen ausgelegt und der Default auf
> `Inclusive` gesetzt. Der exakte Algorithmus ist gegen den offiziellen EBICS-Annex zu
> **verifizieren**, sobald die Schemas vorliegen; M2 (Krypto/Signaturen) wählt die Methode
> dann über die `@Algorithm`-URI.

## Verhältnis zu `CanonicalXmlComparer`

Der Test-Helfer [`CanonicalXmlComparer`](../development/testing.md#canonicalxmlcomparer--kanonisierter-xml-vergleich)
delegiert seit #15 an `XmlCanonicalizer` (Modus `Inclusive`) — es gibt **eine**
C14N-Implementierung. Der Helfer bleibt zusätzlich whitespace-tolerant, weil er
Serializer-Determinismus vergleicht, nicht signierte Bytes erzeugt.

## Tests

`tests/EBICO.Tests/Serialization/` (Tier A, CI-sicher, ohne proprietäre Beispiele):

- `XmlCanonicalizerTests` — bekannte C14N-Vektoren (angelehnt an W3C C14N 1.0 §3 / exc-c14n,
  DTD-frei): Attribut-/Namespace-Sortierung, leeres Element ↔ explizites Schließen,
  Zeichen-Escaping im Text, UTF-8-Oktette, Kommentar-Modi, **Inklusiv-vs-Exklusiv-Differenzierer**,
  Determinismus, DOCTYPE-/`null`-/Malformed-Härtung; dazu `C14nAlgorithms`-Mapping.
- `EbicsXmlSerializerTests` — deterministische, BOM-/xsi-/xsd-freie Ausgabe je
  H003/H004/H005, strukturgleich über die Versionen, stabiler `ds`-Präfix bei
  `AuthSignature`, Round-Trip über `DeserializeEnvelope` und XXE-Härtung.

## Verwandtes

- [XSD-Bindings](xsd-bindings.md) — die generierten Klassen, die hier serialisiert werden
- [Versions-Dispatch](version-dispatch.md) — `EbicsVersionDetector`/Registry, auf denen das
  Deserialisieren aufsetzt
- [Test-Harness](../development/testing.md) — `CanonicalXmlComparer` (delegiert hierher)
- [ADR-0003 — proprietäre Schemas](../adr/0003-umgang-mit-proprietaeren-schemas.md) ·
  [ADR-0006 — Bindings committen](../adr/0006-generierte-xsd-bindings-committen.md)
