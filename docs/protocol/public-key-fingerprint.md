# Public-Key-Fingerprints (Hashwerte öffentlicher Schlüssel) (H003/H004/H005)

Der EBICS-**Public-Key-Fingerprint** in `EBICO.Core` (`Crypto/`): der **SHA-256-Hashwert**
eines öffentlichen RSA-Schlüssels. Er wird an drei Stellen gebraucht — im **INI-Brief**
(menschliche Sichtprüfung des übertragenen Schlüssels bei der Bank), in der **HPB-Antwort**
(der Client verifiziert die zurückgelieferten Bankschlüssel) und in den **`BankPubKeyDigests`**
des Request-Headers (der Server verifiziert die vom Client mitgesendeten Hashes der ihm bekannten
Bankschlüssel). Baut auf der Schlüssel-Schicht aus [#18](key-representation.md) auf und nutzt den
in [#19](bank-signature.md) etablierten SHA-256-Baustein. Issue **#22** (Milestone M2),
Krypto-Bibliothek: [ADR-0008](../adr/0008-krypto-bibliothek.md)
(`System.Security.Cryptography`, kein BouncyCastle) — SHA-256 kommt nativ aus der BCL.

> **Abgrenzung:** Bewusst nur die **Byte-Ebene** — Fingerprint berechnen (`Compute`), gegen einen
> übermittelten Hash konstantzeitig verifizieren (`Verify`) und die Hex-Darstellung für den
> INI-Brief liefern (`ToLetterFormat`). Das **XML-Assembly** der Digest-Elemente
> (`PubKeyDigestType`, `StaticHeaderTypeBankPubKeyDigests`, `EncryptionPubKeyDigest`), die
> INI/HIA/HPB-Order-Data-Bindung und das **volle INI-Brief-Dokument** (Subscriber-IDs,
> Schlüsselversionen, Datum, Unterschriftszeile) gehören **nicht** hierher — sie sind Sache der
> Dispatch-/Onboarding-Schicht (M3) bzw. der Suite (M7). Der aus [#21](encryption-e002.md)
> verschobene `EncryptionPubKeyDigest`-Baustein wird hier über die zwei Zutaten aufgelöst, die die
> DTO braucht: `Compute(...)` (die `Value`-Bytes) und `DigestAlgorithm` (der `@Algorithm`-String).
> Die literale DTO-Befüllung ist dann in M3 ein Dreizeiler:
>
> ```csharp
> new Schema.H004.DataEncryptionInfoTypeEncryptionPubKeyDigest {
>     Value     = PublicKeyFingerprint.Compute(bankEncKey),
>     Algorithm = PublicKeyFingerprint.DigestAlgorithm,
>     Version   = "E002",
> };
> ```

## Bausteine

Unter `src/EBICO.Core/Crypto/` (Namespace `EBICO.Core.Crypto`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `PublicKeyFingerprint` (static) | `PublicKeyFingerprint.cs` | Fingerprint berechnen (`Compute`), Hash-Input bauen (`BuildHashInput`), konstantzeitig verifizieren (`Verify`), INI-Brief-Hex rendern (`ToLetterFormat`); Konstanten `HashAlgorithm` (SHA-256) und `DigestAlgorithm` (Wire-`@Algorithm`-URI) |

Wiederverwendet aus [#18](key-representation.md): `RsaKeyMaterial` (`Modulus`/`Exponent` in
kanonischer Form, `ToPublicOnly()`), `RsaKeyImportExport` (`ImportRsaKeyValue`,
`ImportPublicKeyFromCertificate`) sowie `KeyMaterialException`. Der Konstantzeit-Vergleich
(`CryptographicOperations.FixedTimeEquals`) folgt dem Muster der X002-Signatur
([#20](auth-signature-x002.md)).

## Fingerprint — Verfahren

Der Hashwert wird über eine **ASCII-Zeichenkette** aus Exponent und Modulus gebildet. Jeder der
beiden wird als **Hex** dargestellt (führende Nullen entfernt), sie werden — Exponent zuerst —
durch **ein einzelnes Leerzeichen** getrennt, und über diese ASCII-Bytes läuft **SHA-256**. Die
Eingabe-Bytes kommen aus `RsaKeyMaterial.Exponent`/`.Modulus`, die bereits **kanonisch** sind
(vorzeichenloses Big-Endian, ohne führendes Null-Byte), sodass der Fingerprint mit der
Order-Data-Schicht dieselben Bytes sieht.

| Schritt | Ein-/Ausgabe | BCL |
|---|---|---|
| Hex je Zahl | `Exponent`/`Modulus` (Bytes) → Kleinbuchstaben-Hex, führende Null-**Nibbles** gestrippt | `Convert.ToHexStringLower(...).TrimStart('0')` |
| Hash-Input | `"<exponent-hex> <modulus-hex>"` (ASCII) | `Encoding.ASCII.GetBytes(...)` |
| Fingerprint | 32-Byte SHA-256-Digest | `SHA256.HashData(...)` |
| Wire (`PubKeyDigestType/@Value`) | Digest → base64 | XML-Serialisierung (M3) |
| INI-Brief | Digest → gruppiertes Großbuchstaben-Hex | `ToLetterFormat(...)` |

```csharp
// Fingerprint eines beliebigen öffentlichen Schlüssels (versionsagnostisch):
byte[] digest = PublicKeyFingerprint.Compute(bankKey);   // 32 Byte SHA-256

// Verifikation eines vom Gegenüber gesendeten Hashes (konstantzeitig, kein Throw):
bool ok = PublicKeyFingerprint.Verify(bankKey, clientSentDigest);

// Darstellung für den INI-Brief:
string letter = PublicKeyFingerprint.ToLetterFormat(digest);
```

Für den Exponenten 65537 (`0x010001`) ergibt der Nibble-Strip den Hex-String `10001` — nicht
`010001` — und der Hash-Input beginnt entsprechend mit `10001 …`.

> **⚠️ Spec-Vorbehalt:** Die exakte Formatierung des Hash-Inputs — **Reihenfolge**
> Exponent-vor-Modulus, **Hex-Schreibweise** (Kleinbuchstaben) und das **Strippen führender
> Null-Nibbles** plus Ein-Leerzeichen-Trenner — sind EBICS-Spec-Details, die **noch nicht gegen
> die offiziellen Schemas/Annexe verifiziert** sind (vgl. CLAUDE.md). Sie sind auf die einzige
> Stelle `NormalizeHashInput` begrenzt und werden dort nachgezogen, sobald die Spec vorliegt. Da
> `Compute` **und** `BuildHashInput` durch diese Stelle laufen, ist eine Umstellung eine
> Ein-Stellen-Änderung. Ebenso ist die `DigestAlgorithm`-URI
> (`http://www.w3.org/2001/04/xmlenc#sha256`, identisch zur X002-`DigestMethodAlgorithm`) als
> Konstante gekapselt.

## INI-Brief-Darstellung

`ToLetterFormat` rendert den 32-Byte-Digest als **Großbuchstaben-Hex**, Byte-Paare durch ein
Einzelleerzeichen getrennt, **8 Bytes pro Zeile** — also vier Zeilen für SHA-256:

```
73 16 CA CB 34 AD CD 7D
A8 2B 17 32 AB F5 0B D0
67 AB 7C 14 40 3F 88 28
A1 06 8D BE 04 2D 77 F1
```

Die Gruppierung ist rein **kosmetisch** (die Sichtprüfung durch den Bankmitarbeiter) und **kein**
Spec-Vorbehalt: der Wire nutzt base64 der Rohbytes, der gedruckte Brief das Hex. Wer ungruppiertes
Hex braucht, nutzt direkt `Convert.ToHexString(digest)`.

## Fehlerverhalten

| Bedingung | Verhalten |
|---|---|
| `key == null` (bei `Compute`/`BuildHashInput`/`Verify`) | `ArgumentNullException` |
| `Verify` mit falschem Digest (Inhalt weicht ab) | `false` |
| `Verify` mit abweichender Länge (abgeschnitten/überlang) | `false` (via `FixedTimeEquals`) |

> **`false`-statt-Werfen bei `Verify`:** wie `BankSignature.Verify` liefert ein schlechter,
> client-gesendeter Digest eine saubere Ablehnung (`false`), keine Exception — nur `key == null`
> wirft. Fingerprints sind **nicht geheim**, der Konstantzeit-Vergleich ist hier nicht
> sicherheitskritisch, folgt aber der Projektkonvention aus [#20](auth-signature-x002.md).

## EBICS-Versionsbezug

Der Fingerprint ist **versionsagnostisch**: er berührt nie XML und sieht ausschließlich ein
`RsaKeyMaterial`. Die Protokollversion entscheidet nur, **woher** dieses Material stammt:

| Version | Wire-Quelle des Public Keys | Weg zu `RsaKeyMaterial` |
|---|---|---|
| H003 / H004 | `PubKeyInfoType/PubKeyValue/RSAKeyValue` (Modulus/Exponent, base64) | `RsaKeyImportExport.ImportRsaKeyValue(mod, exp)` |
| H005 | `PubKeyInfoType/X509Data` (Zertifikat, **kein** `PubKeyValue`) | `RsaKeyImportExport.ImportPublicKeyFromCertificate(cert)` |

EBICS 3.0 (H005) ist zertifikatsbasiert; der öffentliche RSA-Schlüssel wird dort aus dem
Zertifikat gelesen. In beiden Fällen liefert der Import **dieselben kanonischen Modulus-/
Exponent-Bytes** und damit **denselben** Fingerprint. Die Zuordnung XML → `RsaKeyMaterial` gehört
in die Dispatch-/Onboarding-Schicht (M3), nicht in diese Primitive.

## Tests

`tests/EBICO.Tests/Crypto/PublicKeyFingerprintTests.cs` (Tier A, CI-sicher, ohne proprietäre
Beispiele; derselbe fixe 2048-Bit-Schlüssel wie in `BankSignatureTests`/`EncryptionE002Tests`):

- **Known-Answer-Vektoren** (deterministisch, byte-genau gepinnt): der Fingerprint-Digest, der
  ASCII-Hash-Input (isoliert die Normalisierungs-Naht von SHA-256) und die INI-Brief-Hex-Form.
- **Nibble-Strip:** der Hash-Input beginnt mit `10001 ` (Exponent 65537), nicht `010001`.
- Happy Path / Selbstkonsistenz: 32-Byte-Länge, Determinismus, `ToPublicOnly()` == Schlüsselpaar,
  sowie **Versionsäquivalenz** (Fingerprint via Zertifikat == via rohem RSA-Schlüssel).
- Verify-Negativfälle: falscher Digest, abgeschnittener Digest, fremder Schlüssel, `null`-Schlüssel.

## Verwandtes

- [Schlüsselpaare & -repräsentation (A/E/X)](key-representation.md) — die zugrunde liegende Schlüssel-Schicht (#18)
- [Banktechnische Signatur A005/A006](bank-signature.md) — teilt den SHA-256-Baustein (#19)
- [Verschlüsselung E002](encryption-e002.md) — verschob den `EncryptionPubKeyDigest`-Baustein hierher (#21)
- [ADR-0008 — Krypto-Bibliothek](../adr/0008-krypto-bibliothek.md)
