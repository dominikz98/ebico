# 0029 — Interop-Fixes für reale Clients (`OrderDetails` ohne `xsi:type`, `A006` auf H004, Modulus-Normalisierung)

- Status: accepted
- Datum: 2026-07-22

## Kontext

[ADR-0026](0026-konformitaet-gegen-reale-clients.md) hat den Vendor-Capture-Tier eingeführt und dabei
bewusst **nur dokumentiert statt gefixt** (dort Entscheidung 3, „Abweichungen dokumentieren statt
Protokoll fixen"). Der Replay der node-ebics-client-Captures zeigte: EBICO nimmt **keinen einzigen**
Onboarding-Request eines realen Fremd-Clients an. Issue **#117** holt den Fix nach.

Drei ursächlich unabhängige Defekte lagen hintereinander auf demselben Pfad — jeder verdeckte den
nächsten, weshalb sie nur nacheinander sichtbar wurden:

1. **`OrderDetails` verlangt `xsi:type`.** `xscgen` übersetzt eine XSD-`<restriction>`, die ein Element
   konkreter typisiert, nicht: `OrderDetails` bleibt im Static-Header von `ebicsUnsecuredRequest` /
   `ebicsNoPubKeyDigestsRequest` auf dem **abstrakten** `OrderDetailsType` stehen. Der `XmlSerializer`
   verlangt dann einen `xsi:type`-Diskriminator. EBICOs eigener Connector emittierte ihn — deshalb war
   EBICO↔EBICO grün —, ein realer Client folgt dem konkreten Schematyp und lässt ihn weg.
2. **Fehlklassifikation.** Das daraus resultierende, nicht abbildbare Client-XML wurde mit
   `061099 EBICS_INTERNAL_ERROR` beantwortet: EBICO gab **sich selbst** die Schuld an einem fremden
   Dokument. Der `EbicsErrorMapper` fing nur `InvalidOperationException { InnerException: XmlException }`;
   die XmlSerializer-Typausnahme trägt einen anderen Inner-Typ.
3. **`A006`/PSS nur auf H005** — node-ebics-client signiert seine H004-INI-Order-Data per Default mit
   `A006`.
4. **Modulus mit ASN.1-Vorzeichen-Byte** (erst nach 1.–3. sichtbar). `ds:Modulus` ist per XML-DSig ein
   `CryptoBinary` ohne führende Null; reale Clients senden bei gesetztem höchsten Bit trotzdem die
   257-Byte-INTEGER-Form. `RsaKeyMaterial` normalisierte zwar die **nach außen** sichtbaren Bytes
   (Fingerprint, `KeySizeBits`), importierte aber die **rohen** Parameter — das ergab einen 2056-Bit-
   Schlüssel, dessen OAEP-Operationen scheiterten. HPB konnte die Bankschlüssel deshalb nicht
   verschlüsseln (`090004`).

## Entscheidung

**1. `OrderDetailsType` wird konkret (Basistyp statt XSD-treuer Abflachung).** In den generierten
Bindings aller drei Versionen entfällt das `abstract`. Die `[XmlInclude]`-Attribute und die konkreten
Sub-Typen bleiben stehen: `xsi:type` wird weiterhin **akzeptiert**, aber nicht mehr **verlangt**. Die
Sub-Typen (`UnsecuredReqOrderDetailsType`, `NoPubKeyDigestsReqOrderDetailsType`,
`UnsignedReqOrderDetailsType`) tragen in H003/H004/H005 **keine eigenen Member** — es geht kein
Informationsgehalt verloren.

**2. Der Eingriff lebt im Generator-Skript, nicht nur im committeten `.cs`.**
`scripts/generate-bindings.sh` wendet nach jedem Lauf `apply_binding_fixups()` an (awk, CRLF-erhaltend)
und **bricht hart ab**, wenn das erwartete Muster fehlt. Dazu ein Guard-Test
(`OrderDetailsBindingTests`), der `IsAbstract == false` prüft — ein verlorener Fixup fällt damit sofort
auf und nicht erst beim nächsten Fremd-Client.

**3. Der Connector emittiert den Basistyp.** Eine Sub-Klassen-Instanz würde weiter `xsi:type` (und die
`xmlns:xsi`-Deklaration) erzeugen. EBICOs Onboarding-Requests sehen damit aus wie die eines realen
Clients — Toleranz in beide Richtungen, nicht nur beim Empfang.

**4. Fehlklassifikation an der Envelope-Grenze lösen, nicht im Error-Mapper.**
`EbicsXmlSerializer.DeserializeEnvelope` übersetzt `XmlSerializer`-Abbildungsfehler in
`EbicsEnvelopeFormatException` (→ `091010 EBICS_INVALID_XML`). Das ist die einzige Stelle, die *weiß*,
dass die Bytes vom Client stammen. Bewusst **nicht** in `DeserializeCore`: die generischen
`Deserialize<T>`-Überladungen dekodieren auch Order-Data, wo `OrderDataFault` bereits gezielt auf
`090004` mappt — eine Übersetzung dort würde dieses Mapping überschreiben.

**5. `A006` gilt für H004 **und** H005.** H003 (EBICS 2.4) bleibt ausgeschlossen.

**6. `RsaKeyMaterial` importiert aus der kanonischen Form.** Modulus/Exponent werden getrimmt, *bevor*
sie in die für `CreateRsa()` gehaltenen `RSAParameters` gehen. Damit stimmen die drei Sichten auf
denselben Schlüssel (exponierte Bytes, `KeySizeBits`, importierte RSA-Instanz) wieder überein.

**7. Der Vendor-Replay wird vom Charakterisierungs- zum Konformitätstest.**
`VendorCaptureConformanceTests` seedet die Stammdaten und treibt die drei Captures als **eine
sequenzielle Kette** INI → HIA → HPB bis `SubscriberState.Ready` samt verschlüsselter HPB-Antwort.

## Konsequenzen

- **Reale Clients funktionieren.** Die Kompatibilitätsmatrix in
  [Konformität gegen reale Clients](../development/conformance-real-clients.md) steht für
  node-ebics-client 5.0.0 / H004 auf ✅ ✅ ✅. Abweichung 1 und 2 aus #59 sind geschlossen.
- **EBICOs eigenes Wire-Format ändert sich** (Onboarding-Requests ohne `xsi:type`/`xmlns:xsi`). Das ist
  durch die E2E-Suite (#57) über H003/H004/H005 abgesichert und macht die Ausgabe strikter konform zur
  in [Serialisierung & C14N](../protocol/serialization-c14n.md) zugesagten xsi-freien Form.
- **Das Binding ist an dieser Stelle laxer als die XSD** — es akzeptiert `OrderDetails` auch dort, wo die
  XSD einen bestimmten konkreten Typ vorschreibt. Praktisch kostenlos: der `XmlSerializer` validiert
  ohnehin nicht gegen die XSD; echte Schema-Validierung bleibt der Tier-B-Test
  `SchemaValidationConformanceTests` (skip-if-missing).
- **Spec-Vorbehalt bleibt.** Weder die Konkretisierung von `OrderDetails` noch `A006` auf H004 sind gegen
  die offiziellen XSDs/Annexe verifiziert (proprietär, nicht im Repo — [ADR-0003](0003-umgang-mit-proprietaeren-schemas.md)).
  Die Evidenz ist ein realer Client plus die verbreitete Lesart (EBICS 2.5 Annex 1 kennt A005 **und**
  A006). Beides ist an genau einer Stelle zentralisiert (`apply_binding_fixups()` bzw. `KeyVersions`) und
  bei besserer Faktenlage in einem Schritt revidierbar.
- **Der Generator ist kein reiner Generator mehr.** Wer die Bindings neu erzeugt, muss den Fixup-Schritt
  kennen; siehe [XSD-Bindings](../protocol/xsd-bindings.md), Abschnitt „Manuelle Fixups".

## Alternativen

- **Header-Klassen XSD-treu abflachen** (`OrderDetails` + `SecurityMedium` aus `StaticHeaderBaseType` in
  die drei abgeleiteten Header-Typen ziehen, dort konkret typisiert): verworfen — bildet die
  XSD-`restriction` zwar korrekt ab, betrifft aber 12 generierte Dateien statt 3, und die
  Serialisierungsreihenfolge (Basis-Member vor abgeleiteten) sowie die Position der
  `xs:any`-Collection müssten von Hand stimmen. Deutlich mehr Risiko für denselben Wire-Effekt.
- **`XmlAttributeOverrides` pro Envelope-Wurzeltyp:** verworfen — die Bindings blieben unberührt, aber
  die Übersteuerung eines geerbten Members ist im `XmlReflectionImporter` nicht klar spezifiziert, und
  jede Version × Wurzel bräuchte einen eigenen Override-Satz plus eigenen Serializer-Cache.
- **Nur empfangsseitig tolerant sein (weiter `xsi:type` emittieren):** verworfen — löst nur die Hälfte.
  Ein strikter Fremd-Parser auf der Gegenseite (echte Bank) bekäme weiter EBICOs Diskriminator.
- **Den `EbicsErrorMapper` um `InvalidOperationException` erweitern:** verworfen — zu breit. Ein
  `InvalidOperationException` aus dem Server-Inneren ist ein echter Serverfehler und muss `061099`
  bleiben; nur an der Envelope-Grenze ist die Zuordnung eindeutig.
- **`A006` auf H004 offen lassen:** verworfen — die INI eines realen Clients wäre weiter abgelehnt, und
  der Vendor-Replay hätte den nachgelagerten Modulus-Defekt nie aufgedeckt.

## Verwandte Entscheidungen

- [ADR-0026 — Konformität gegen reale Clients](0026-konformitaet-gegen-reale-clients.md) — hat diese
  Funde erzeugt und den Fix ausdrücklich als Folgearbeit zurückgestellt.
- [ADR-0006 — Generierte XSD-Bindings committen](0006-generierte-xsd-bindings-committen.md) — warum die
  Bindings überhaupt im Repo liegen und ein Fixup-Schritt nötig ist.
- [ADR-0003 — Umgang mit proprietären Schemas](0003-umgang-mit-proprietaeren-schemas.md) — warum die
  Verifikation gegen XSD/Annexe hier nicht möglich ist.
