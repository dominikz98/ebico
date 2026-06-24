# Verschlüsselung E002 (RSA-OAEP + AES-128-CBC) (H003/H004/H005)

Die EBICS-Transportverschlüsselung in `EBICO.Core` (`Crypto/`): ein **hybrides** Verfahren —
die Auftragsdaten werden symmetrisch mit einem Einmal-**Transaktionsschlüssel** (AES-128-CBC)
verschlüsselt, und dieser Transaktionsschlüssel wird asymmetrisch mit dem öffentlichen
Verschlüsselungsschlüssel (`E`) des Empfängers per **RSAES-OAEP über SHA-256** verschlüsselt.
Baut auf der Schlüssel-Schicht aus [#18](key-representation.md) auf. Issue **#21** (Milestone M2),
Krypto-Bibliothek: [ADR-0008](../adr/0008-krypto-bibliothek.md)
(`System.Security.Cryptography`, kein BouncyCastle).

> **Abgrenzung:** Bewusst nur die **Byte-Ebene** der hybriden Verschlüsselung — die beiden
> Chiffrate (verschlüsselter Transaktionsschlüssel + verschlüsselte Auftragsdaten). Das
> `DataEncryptionInfo`-/`EncryptionPubKeyDigest`-XML-Assembly (#22), die Segmentierung/der
> `DataTransfer`-Envelope, die X002-Authentifikationssignatur (#20) und die banktechnische
> Signatur, die die Integrität/Authentizität liefert (#19), gehören **nicht** hierher. Diese
> Schicht liefert nur die Chiffrat-Bytes, die jene Schichten auf `DataEncryptionInfoType`
> zusammensetzen.

## Bausteine

Unter `src/EBICO.Core/Crypto/` (Namespace `EBICO.Core.Crypto`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `EncryptionE002` (static) | `EncryptionE002.cs` | Transaktionsschlüssel erzeugen, RSA-OAEP über den Schlüssel, AES-128-CBC über die Daten, kombinierter Hybrid-Flow (zustandslose BCL-Wrapper) |
| `EncryptedOrderData` (record struct) | `EncryptionE002.cs` | Ergebnis von `Encrypt`: verschlüsselter Transaktionsschlüssel + verschlüsselte Auftragsdaten |

Wiederverwendet aus [#18](key-representation.md): `RsaKeyMaterial` (`CreateRsa()`,
`HasPrivateKey`, `ToPublicOnly()`), die `KeyVersions`-Registry (`TryGet`, `PaddingIntent`),
`KeyPurpose` sowie `KeyMaterialException`.

## E002 — Verfahren

EBICS verschlüsselt die Auftragsdaten **symmetrisch** mit einem zufälligen Einmal-Schlüssel
(AES-128) im CBC-Modus und verschlüsselt diesen Schlüssel **asymmetrisch** mit dem öffentlichen
`E`-Schlüssel des Empfängers. Die RSA-Padding-Variante hängt an der Schlüsselversion und wird
**registry-getrieben** aus `KeyVersionInfo.PaddingIntent` aufgelöst (nicht hartkodiert):

| Schritt | Schema | BCL | Determinismus |
|---|---|---|---|
| Transaktionsschlüssel | AES-128 (16 Byte), zufällig | `RandomNumberGenerator.GetBytes(16)` | randomisiert |
| Schlüssel-Verschlüsselung | RSAES-OAEP über SHA-256 | `RSAEncryptionPadding.OaepSHA256` | randomisiert |
| Auftragsdaten | AES-128-CBC, PKCS7-Padding, Null-IV | `Aes.EncryptCbc(data, ivZero, PKCS7)` | deterministisch (gleicher Schlüssel/IV/Klartext → gleiches Chiffrat) |

OAEP-SHA256 auf einem 2048-Bit-Schlüssel fasst bis zu 190 Byte Klartext — ein 16-Byte-AES-Schlüssel
passt bequem. RSA-OAEP läuft über `RSA.Encrypt`/`RSA.Decrypt`, die AES-Schicht über
`Aes.EncryptCbc`/`Aes.DecryptCbc`.

```csharp
// Voll-Hybrid (die übliche Schicht):
var enc = EncryptionE002.Encrypt(orderData, recipientPubKey, KeyVersion.Create("E002"));
byte[] back = EncryptionE002.Decrypt(enc, recipientKey, KeyVersion.Create("E002"));

// Primitive einzeln (z. B. wenn ein Transaktionsschlüssel über Segmente wiederverwendet wird):
var tk        = EncryptionE002.GenerateTransactionKey();                 // 16-Byte AES-Schlüssel
var encData   = EncryptionE002.EncryptOrderData(orderData, tk);          // AES-128-CBC
var encTk     = EncryptionE002.EncryptTransactionKey(tk, recipientPubKey, KeyVersion.Create("E002")); // RSA-OAEP
var tkBack    = EncryptionE002.DecryptTransactionKey(encTk, recipientKey, KeyVersion.Create("E002"));
var dataBack  = EncryptionE002.DecryptOrderData(encData, tkBack);
```

`GenerateTransactionKey` und die beiden Primitiv-Paare sind **öffentlich**, weil der Voll-Hybrid
durch das randomisierte RSA-OAEP nicht byte-genau pinbar ist; nur die deterministische AES-Schicht
lässt sich mit einem festen Schlüssel als Known-Answer-Vektor verankern.

## IV & Padding — Spec-Vorbehalt

> **⚠️ Spec-Vorbehalt (symmetrisch):** Der **Null-IV** (16 Null-Bytes) und das **PKCS7-Padding**
> der Auftragsdaten-Verschlüsselung sind EBICS-Spec-Details, die **noch nicht gegen die offiziellen
> Schemas/Annexe verifiziert** sind (vgl. CLAUDE.md). Sie sind auf eine einzige Stelle begrenzt
> (`TransactionIv`, `SymmetricPadding`) und werden dort nachgezogen, sobald die Spec vorliegt. Da
> **sowohl** `EncryptOrderData` als auch `DecryptOrderData` durch diese Stelle laufen, bleiben in
> sich konsistente Encrypt-→-Decrypt-Round-Trips davon unberührt.

> **⚠️ Spec-Vorbehalt (RSA-Padding):** Die EBICS-Version `E002` hat den Transaktionsschlüssel in
> manchen historischen Spec-Revisionen mit **RSAES-PKCS1-v1_5** statt OAEP verschlüsselt. EBICO
> folgt der Registry-Intention (**OAEP-SHA256**, wie in diesem Issue gefordert). Das Padding kommt
> aus `KeyVersions` (`E002 → RsaPaddingScheme.Oaep`) und ist **nie** in der Primitive hartkodiert —
> sollte echte Bank-Interop PKCS1-v1.5 erfordern, ist das eine **Ein-Zeilen-Änderung in
> `KeyVersions.cs`**, kein Eingriff in `EncryptionE002`. ADR-0008 sieht diese Revision bereits vor.

## Fehlerverhalten

| Bedingung | Verhalten |
|---|---|
| `key == null` / `recipientKey == null` | `ArgumentNullException` |
| Entschlüsseln (Transaktionsschlüssel) ohne privaten Schlüssel | `KeyMaterialException` |
| `version` keine bekannte **Verschlüsselungs**-Version mit OAEP (`A005`, `X002`, `E001`, `A999`, `default`) | `InvalidOperationException` |
| Transaktionsschlüssel-Länge ≠ 16 Byte | `ArgumentException` |
| Entschlüsseln mit falschem Schlüssel / manipuliertes RSA-Chiffrat | `CryptographicException` (OAEP-Integritätsprüfung) |
| Entschlüsseln eines manipulierten AES-Chiffrats (letzter Block) | `CryptographicException` (PKCS7-Padding ungültig) |

> **Kein `false`-statt-Werfen-Pfad:** Anders als `BankSignature.Verify` hat die Ver-/Entschlüsselung
> kein boolesches Urteil — **jeder** Fehler wirft. CBC bietet **keine Integrität**: ein in einem
> früheren Block manipuliertes AES-Chiffrat liefert verfälschten, aber „gültig gepaddeten" Klartext
> ohne Exception. Integrität/Authentizität liefert die banktechnische Signatur (#19), nicht E002.

> **Keine Versions-Permission-Prüfung hier:** Ob E002 mit einer EBICS-Protokollversion erlaubt ist,
> bleibt Aufgabe von `KeyVersions.EnsurePermitted` in der Dispatch-/Onboarding-Schicht. Diese
> Primitive bleibt policy-frei und löst **kein** `KeyVersionNotPermittedException` aus.

## EBICS-Versionsbezug

Das Verfahren (AES-128-CBC + RSA-OAEP-Transaktionsschlüssel) ist über H003/H004/H005 identisch;
E002 ist in allen dreien zulässig (zentral in [`KeyVersions`](key-representation.md)). Die
Legacy-Version E001 (RSAES-PKCS1-v1.5) ist **nicht** Ziel dieses Issues — sie wird von der
`ResolveEncryptionPadding`-Stelle bewusst abgelehnt (`InvalidOperationException`).

## Tests

`tests/EBICO.Tests/Crypto/EncryptionE002Tests.cs` (Tier A, CI-sicher, ohne proprietäre Beispiele):

- Happy Path: Voll-Hybrid Encrypt → Decrypt, RSA-OAEP-Primitiv-Round-Trip, AES-Primitiv-Round-Trip.
- Verschlüsseln mit `ToPublicOnly()` → Entschlüsseln mit Schlüsselpaar (privater Schlüssel beim
  Verschlüsseln nicht nötig).
- `Encrypt` liefert beide Chiffrate; verschlüsselter Transaktionsschlüssel == 256 Byte (2048-Bit-Modulus).
- **Deterministischer AES-128-CBC-Known-Answer-Vektor**: fixer 16-Byte-Schlüssel + feste
  Auftragsdaten → byte-gleiches Chiffrat (pinnt Null-IV und PKCS7), plus Decrypt-Richtung.
- **RSA-OAEP-Nichtdeterminismus**: zweimal verschlüsseln → unterschiedliche Chiffrate, beide
  entschlüsseln; OAEP-Round-Trip mit fixem PKCS#8-Schlüssel.
- Negativfälle: Entschlüsseln ohne privaten Schlüssel, falscher Schlüssel, manipuliertes RSA- bzw.
  AES-Chiffrat, falsche Schlüssellänge, `null`-Schlüssel, falsche/unbekannte Version
  (`A005`/`X002`/`E001`/`A999`/`default`).

## Verwandtes

- [Schlüsselpaare & -repräsentation (A/E/X)](key-representation.md) — die zugrunde liegende Schlüssel-Schicht (#18)
- [Banktechnische Signatur A005/A006](bank-signature.md) — die schwester-Krypto-Operation, liefert die Integrität (#19)
- [ADR-0008 — Krypto-Bibliothek](../adr/0008-krypto-bibliothek.md)
