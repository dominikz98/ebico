# Banktechnische Signatur A005/A006 (H003/H004/H005)

Die erste echte Krypto-**Operation** in `EBICO.Core` (`Crypto/`): die banktechnische
(autorisierende) Signatur über Auftragsdaten erzeugen und verifizieren — Schlüsselversion
**A005** (RSASSA-PKCS1-v1.5) und **A006** (RSASSA-PSS), beide über **SHA-256**. Baut auf der
Schlüssel-Schicht aus [#18](key-representation.md) auf. Issue **#19** (Milestone M2),
Krypto-Bibliothek: [ADR-0008](../adr/0008-krypto-bibliothek.md)
(`System.Security.Cryptography`, kein BouncyCastle).

> **Abgrenzung:** Bewusst nur die **Byte-Ebene** der RSA-Signatur plus der Order-Hash
> (SHA-256). Die X002-Authentifikationssignatur (#20), die Verschlüsselung E002 (#21),
> Hashing/Public-Key-Fingerprints (#22) und die X.509-Kettenprüfung (#23) gehören **nicht**
> hierher. Auch das XML-DSig-`SignedInfo`-Envelope, die C14N der signierten XML und das
> `UserSignatureData`/`OrderSignatureData`-Container-Assembly sind auf die Order-Data-/
> Transaktions-Issues verschoben — diese Schicht liefert nur die Signatur-Bytes und den Hash,
> die jene Schichten zusammensetzen.

## Bausteine

Unter `src/EBICO.Core/Crypto/` (Namespace `EBICO.Core.Crypto`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `BankSignature` (static) | `BankSignature.cs` | Order-Hash, Signieren/Verifizieren A005/A006 (zustandslose BCL-Wrapper) |

Wiederverwendet aus [#18](key-representation.md): `RsaKeyMaterial` (`CreateRsa()`,
`HasPrivateKey`, `ToPublicOnly()`), die `KeyVersions`-Registry (`TryGet`, `PaddingIntent`),
`KeyPurpose` sowie `KeyMaterialException`.

## A005 / A006 — Verfahren

EBICS bildet die banktechnische Signatur über einen **SHA-256-Order-Hash** der Auftragsdaten
und signiert diesen mit dem privaten Signaturschlüssel (`A`). Die Padding-Variante hängt an
der Schlüsselversion und wird **registry-getrieben** aus `KeyVersionInfo.PaddingIntent`
aufgelöst (nicht hartkodiert):

| Version | RSA-Schema | BCL-Padding | Determinismus |
|---|---|---|---|
| A005 | RSASSA-PKCS1-v1.5 | `RSASignaturePadding.Pkcs1` | deterministisch (gleiche Eingabe → gleiche Signatur) |
| A006 | RSASSA-PSS | `RSASignaturePadding.Pss` | randomisiert (zufälliges Salt) |

PSS verwendet den BCL-Default (Salt-Länge = Hash-Länge = 32 Byte, MGF1-SHA-256), was der
A006-Erwartung entspricht. Beides läuft über `RSA.SignHash`/`RSA.VerifyHash` mit
`HashAlgorithmName.SHA256`.

```csharp
var hash = BankSignature.ComputeOrderHash(orderData);          // 32-Byte SHA-256-Digest
var sig  = BankSignature.Sign(orderData, signerKey, KeyVersion.Create("A005"));
bool ok  = BankSignature.Verify(orderData, sig, signerPubKey, KeyVersion.Create("A005"));

// Hash explizit (z. B. wenn der Hash anderswo schon vorliegt):
var sig2 = BankSignature.SignHash(hash, signerKey, KeyVersion.Create("A006"));
bool ok2 = BankSignature.VerifyHash(hash, sig2, signerPubKey, KeyVersion.Create("A006"));
```

`ComputeOrderHash` ist **öffentlich**, damit die Order-Data- und die Fingerprint-Schicht (#22)
exakt dieselben Bytes verwenden (gleiche Begründung wie der kanonische Modulus/Exponent in #18).

## Order-Hash & Normalisierung

> **⚠️ Spec-Vorbehalt:** Die genaue **Normalisierung** der Auftragsdaten vor dem Hashen (z. B.
> Zeilenende-Normalisierung für bestimmte Formate) ist ein EBICS-Spec-Detail, das **noch nicht
> gegen die offiziellen Schemas/Annexe verifiziert** ist (vgl. CLAUDE.md). Sie ist auf eine
> einzige Stelle begrenzt (`NormalizeOrderData`, derzeit Identität/Pass-through) und wird dort
> nachgezogen, sobald die Spec vorliegt. Da **sowohl** `Sign` als auch `Verify` durch diese
> Stelle laufen, bleiben in sich konsistente Sign-→-Verify-Round-Trips davon unberührt.

## Fehlerverhalten

| Bedingung | Verhalten |
|---|---|
| `key == null` (Sign/Verify) | `ArgumentNullException` |
| Signieren ohne privaten Schlüssel | `KeyMaterialException` |
| `version` keine bekannte **Signatur**-Version (`A999`, `E002`, `X002`, `default`) | `InvalidOperationException` |
| Verify: falscher Schlüssel / manipulierte Daten / manipulierte oder zu kurze Signatur | Rückgabe `false` (wirft **nicht**) |

> **Keine Versions-Permission-Prüfung hier:** Ob eine Version mit einer EBICS-Protokollversion
> erlaubt ist (z. B. A006 mit H003), bleibt Aufgabe von `KeyVersions.EnsurePermitted` in der
> Dispatch-/Onboarding-Schicht. Diese Primitive bleibt policy-frei und löst **kein**
> `KeyVersionNotPermittedException` aus. Der `false`-statt-Werfen-Pfad beim Verifizieren hält
> den Server robust: eine fehlerhafte Client-Signatur ist eine saubere Ablehnung, kein Crash.

## EBICS-Versionsbezug

Das Verfahren (SHA-256-Order-Hash + RSA-Signatur) ist über H003/H004/H005 identisch; nur die
**zulässigen Versionen** unterscheiden sich (A006 ab EBICS 2.5/H004, siehe #117 und
[ADR-0029](../adr/0029-interop-fixes-reale-clients.md)) und liegen zentral in
[`KeyVersions`](key-representation.md). A004 (Legacy) ist über dasselbe PKCS1-v1.5-Mapping
abgedeckt, ist aber nicht Ziel dieses Issues.

## Tests

`tests/EBICO.Tests/Crypto/BankSignatureTests.cs` (Tier A, CI-sicher, ohne proprietäre Beispiele):

- Happy Path A005 und A006 (Sign → Verify).
- Round-Trip über expliziten Hash; `ComputeOrderHash`-Länge == 32.
- Cross-Verify mit `ToPublicOnly()` (privater Schlüssel beim Verifizieren nicht nötig).
- Negativfälle: manipulierte Daten, manipulierte Signatur, zu kurze Signatur, falscher Schlüssel,
  falsche/unbekannte Version (`E002`/`X002`/`A999`/`default`), Signieren ohne privaten Schlüssel,
  `null`-Schlüssel, Cross-Version (A005-Signatur als A006 verifiziert und umgekehrt).
- **Deterministischer A005-Known-Answer-Vektor**: fixer PKCS#8-Schlüssel (derselbe wie in
  `RsaKeyImportExportTests`) + feste Auftragsdaten → byte-gleiche Signatur (pinnt Padding und
  Normalisierung).
- **A006/PSS-Nichtdeterminismus**: zweimal signieren → unterschiedliche Signaturen, beide
  verifizieren.

## Verwandtes

- [Schlüsselpaare & -repräsentation (A/E/X)](key-representation.md) — die zugrunde liegende Schlüssel-Schicht (#18)
- [ADR-0008 — Krypto-Bibliothek](../adr/0008-krypto-bibliothek.md)
