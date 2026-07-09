# Authentifikationssignatur X002 (H003/H004/H005)

Die EBICS-**Authentifikationssignatur** über den Request: eine XML Digital Signature
(`ds:Signature`), die im `AuthSignature`-Element zwischen `header` und `body` steht und
**alle Elemente mit `authenticate="true"`** absichert — Schlüsselversion **X002**
(RSASSA-PKCS1-v1.5 über **SHA-256**, inklusive Canonical XML 1.0). Sie schützt Integrität
und Authentizität des Transports. Baut auf der Schlüssel-Schicht aus
[#18](key-representation.md) und dem Canonicalizer aus [#15](serialization-c14n.md) auf.
Issue **#20** (Milestone M2), Krypto-Bibliothek:
[ADR-0008](../adr/0008-krypto-bibliothek.md) (`System.Security.Cryptography`, kein BouncyCastle).

> **Abgrenzung:** Diese Schicht liefert nur **Erzeugung und Verifikation** der `AuthSignature`
> über ein serialisiertes Request-XML. Sie ist gegenüber der *banktechnischen* Signatur
> A005/A006 ([#19](bank-signature.md), autorisierend über Auftragsdaten) und der Verschlüsselung
> E002 ([#21](encryption-e002.md)) abgegrenzt. Das automatische Setzen der `AuthSignature` im
> Send-/Dispatch-Weg sowie die Interop-Verifikation gegen echte Bank-Beispiele gehören in
> spätere Milestones (M3–M6) — hier bleibt X002 eine policy-freie Krypto-Primitive.

## Bausteine

Unter `src/EBICO.Core/Crypto/` (Namespace `EBICO.Core.Crypto`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `AuthenticationSignature` (static) | `AuthenticationSignature.cs` | `Sign`/`Verify` der X002-`AuthSignature` (zustandslose BCL-Wrapper) |

Wiederverwendet aus [#15](serialization-c14n.md): `XmlCanonicalizer` (C14N als UTF-8-Oktette,
Node-Set-Overload), `C14nMode`/`C14nAlgorithms` (Modus ↔ `@Algorithm`-URI). Aus
[#18](key-representation.md): `RsaKeyMaterial` (`CreateRsa()`, `HasPrivateKey`, `ToPublicOnly()`),
die `KeyVersions`-Registry (`TryGet`, `Purpose`, `PaddingIntent`) sowie `KeyMaterialException`.
Die `ds:`-Objektmodelle (`SignatureType`, `SignedInfoType`, `ReferenceType`, …) stammen aus den
committeten [XSD-Bindings](xsd-bindings.md) unter `src/EBICO.Core/Schema/Shared/XmlDsig/`.

## X002 — Verfahren

Die Signatur enthält **zwei** Hashes:

1. **Reference-Digest** — SHA-256 über die C14N der **authentifizierten Knotenmenge** (die
   `authenticate="true"`-Teilbäume). Ergebnis → `ds:Reference/ds:DigestValue`. Die Reference
   trägt `URI="#xpointer(//*[@authenticate='true'])"`, einen C14N-`ds:Transform` und
   `ds:DigestMethod`.
2. **SignatureValue** — RSA-Signatur (PKCS1-v1.5 über SHA-256) über die C14N des
   `ds:SignedInfo`. Die Padding-Variante wird **registry-getrieben** aus
   `KeyVersionInfo.PaddingIntent` aufgelöst (nicht hartkodiert): X001/X002 → `RSASignaturePadding.Pkcs1`.

| Element | `@Algorithm`-URI |
|---|---|
| `ds:CanonicalizationMethod` / `ds:Transform` | `http://www.w3.org/TR/2001/REC-xml-c14n-20010315` (inklusiv, Default) |
| `ds:SignatureMethod` | `http://www.w3.org/2001/04/xmldsig-more#rsa-sha256` |
| `ds:DigestMethod` | `http://www.w3.org/2001/04/xmlenc#sha256` |

**Dokumentkontext-Kanonisierung.** Beide C14N-Schritte laufen im Kontext des Envelopes: das
signierte Material erbt die Namespace-Deklarationen der Request-Wurzel (Protokoll-Namespace als
Default, `ds`-Präfix). Inklusive C14N rendert diese am Apex der Knotenmenge — der kanonische
Header trägt also z. B. `xmlns="urn:org:ebics:H005"`, genau wie eine Gegenstelle es erzeugt und
erwartet. Für die SignedInfo-C14N wird das (nur mit `ds` präfigierte) `ds:SignedInfo` in einen
Klon des Request-DOM eingehängt und als Teilbaum kanonisiert; **derselbe** Seam bedient Signieren
und Verifizieren, sodass Round-Trips symmetrisch bleiben.

```csharp
// Request serialisieren (AuthSignature noch leer/abwesend), dann signieren:
string requestXml = EbicsXmlSerializer.SerializeToString(request, EbicsVersion.H005);
SignatureType auth = AuthenticationSignature.Sign(requestXml, signerKey, KeyVersion.Create("X002"));
request.AuthSignature = auth;

// Serverseitig verifizieren (über das empfangene Wire-XML + die deserialisierte AuthSignature):
bool ok = AuthenticationSignature.Verify(requestXml, request.AuthSignature, signerPubKey, KeyVersion.Create("X002"));
```

Das `AuthSignature`-Element ist selbst **nicht** `authenticate="true"` und beeinflusst den
Digest daher nicht — `Verify` funktioniert unabhängig davon, ob das übergebene `requestXml` die
Signatur bereits enthält.

## Spec-Vorbehalt

> **⚠️ Spec-Vorbehalt:** Der exakte **C14N-Modus** (inklusiv vs. exklusiv), der
> **Reference-Selektor** (`#xpointer(//*[@authenticate='true'])` und seine XPath-Realisierung
> `(//. | //@*)[ancestor-or-self::*[@authenticate='true']]`) sowie der
> **SignedInfo-Kanonisierungskontext** sind EBICS-Spec-Details, die **noch nicht gegen die
> offiziellen Annexe verifiziert** sind (die XSDs sind proprietär und liegen nicht im Repo —
> vgl. `CLAUDE.md` und [serialization-c14n.md](serialization-c14n.md)). Sie sind auf Konstanten
> bzw. den `c14n`-Parameter begrenzt; der Default ist `Inclusive`. In sich konsistente
> Sign-→-Verify-Round-Trips und der deterministische Known-Answer-Vektor bleiben von der Wahl
> unberührt. Die byte-genaue Interop gegen echte Banken wird über einen Tier-B-Test validiert,
> sobald ein Beispiel lokal vorliegt.

## Fehlerverhalten

| Bedingung | Verhalten |
|---|---|
| `requestXml` / `authSignature` / `key` == `null` | `ArgumentNullException` |
| Signieren ohne privaten Schlüssel | `KeyMaterialException` |
| `version` keine bekannte **Authentifikations**-Version (`A005`, `E002`, `X999`, `default`) | `InvalidOperationException` |
| Verify: falscher Schlüssel, manipuliertes authentifiziertes Element, manipulierte `SignatureValue`/`DigestValue`, fehlendes/leeres `SignedInfo`/`Reference`/`SignatureValue`, unbekannte/nicht unterstützte Algorithmus-URI | Rückgabe `false` (wirft **nicht**) |

> **Keine Versions-Permission-Prüfung hier:** Ob eine Version mit einer EBICS-Protokollversion
> erlaubt ist, bleibt Aufgabe von `KeyVersions.EnsurePermitted` in der Dispatch-/Onboarding-Schicht.
> Diese Primitive bleibt policy-frei. Der `false`-statt-Werfen-Pfad beim Verifizieren hält den
> Server robust: eine fehlerhafte Client-Signatur ist eine saubere Ablehnung, kein Crash.

## EBICS-Versionsbezug

Das Verfahren (Digest über `authenticate="true"` + RSA-Signatur über `SignedInfo`) ist über
H003/H004/H005 identisch. **X002** ist über alle drei Versionen die Standard-Authentifikations­version;
das Legacy **X001** ist über dasselbe PKCS1-v1.5-Mapping abgedeckt, aber nicht Ziel dieses Issues.
Die zulässigen Versionen liegen zentral in [`KeyVersions`](key-representation.md).

## Tests

`tests/EBICO.Tests/Crypto/AuthenticationSignatureTests.cs` (Tier A, CI-sicher, ohne proprietäre Beispiele):

- Happy Path Sign → Verify; Cross-Verify mit `ToPublicOnly()`.
- Mehrere (auch verschachtelte) `authenticate="true"`-Elemente in einem Nicht-EBICS-Namespace
  (belegt die Node-Set-Union und Namespace-Unabhängigkeit).
- Negativfälle (Rückgabe `false`): manipuliertes authentifiziertes Element, manipulierte
  `SignatureValue`/`DigestValue`, falscher Schlüssel, unbekannte `CanonicalizationMethod`-URI,
  falsche `SignatureMethod`-URI, fehlendes `SignedInfo`/`SignatureValue`.
- Exceptions: `null`-Argumente, Signieren ohne Private Key, Nicht-Auth/unbekannte/`default`-Version.
- **Deterministischer X002-Known-Answer-Vektor**: fixes Request-XML + fixer PKCS#8-Schlüssel
  (derselbe wie in `BankSignatureTests`) → byte-gleiche `DigestValue` **und** `SignatureValue`
  (pinnt C14N, SignedInfo-Assembly und Padding).
- Dokumentkontext-Beleg: die kanonische Form der authentifizierten Knoten enthält das geerbte
  `xmlns="urn:org:ebics:H005"`.
- Real-`EbicsRequest`-Round-Trip (serialisieren → signieren → anhängen → deserialisieren → verifizieren).
- Tier-B-Interop gegen ein reales Bank-Sample (`SampleXml.TryLoad`, skippt wenn abwesend).

## Verwandtes

- [Banktechnische Signatur A005/A006](bank-signature.md) — die autorisierende Signatur über Auftragsdaten (#19)
- [Verschlüsselung E002](encryption-e002.md) — hybride Transportverschlüsselung (#21)
- [XML-Serialisierung & C14N](serialization-c14n.md) — Canonicalizer und C14N-Modi (#15)
- [Schlüsselpaare & -repräsentation (A/E/X)](key-representation.md) — die zugrunde liegende Schlüssel-Schicht (#18)
- [ADR-0008 — Krypto-Bibliothek](../adr/0008-krypto-bibliothek.md)
