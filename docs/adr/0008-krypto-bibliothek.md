# 0008 — Krypto-Bibliothek: `System.Security.Cryptography`

- Status: accepted
- Datum: 2026-06-22

## Kontext

Mit Milestone **M2 (Krypto)** beginnt die kryptografische Schicht von EBICO. Issue **#18**
(Schlüsselpaare & -repräsentation) ist der erste Krypto-Task; die folgenden Issues setzen
darauf auf: A005/A006-Signatur (#19), X002-Authentifikationssignatur (#20), E002-Verschlüsselung
mit RSA + AES (#21), Hashing/Public-Key-Fingerprints (#22) und X.509-Zertifikatsprüfung (#23).

EBICS nutzt durchgängig RSA: Signaturversionen A004/A005 (RSASSA-PKCS1-v1_5) und A006
(RSASSA-PSS), Authentifikation X001/X002 (RSASSA-PKCS1-v1_5), Verschlüsselung E002
(RSAES-OAEP für den Transaktionsschlüssel, AES-128-CBC für die Auftragsdaten) sowie SHA-256
für Hashes/Fingerprints. Schlüssel- und Zertifikatsaustausch erfolgt über PKCS#8 (private
Schlüssel) und X.509/SubjectPublicKeyInfo (öffentliche Schlüssel).

Im ADR-Backlog war offen, ob dafür die BCL (`System.Security.Cryptography`) genügt oder ein
externes Paket (BouncyCastle) nötig ist. Bisher referenziert `EBICO.Core` nur
`System.Security.Cryptography.Xml` (für C14N, #15); die Test-Infrastruktur
(`TestCertificates`) erzeugt RSA-Schlüssel/Zertifikate bereits in-process mit der BCL.

## Entscheidung

EBICO verwendet für alle kryptografischen Operatonen in M2 ausschließlich
**`System.Security.Cryptography`** aus dem .NET-Framework. **Kein** zusätzliches Krypto-Paket
(insbesondere kein BouncyCastle) wird aufgenommen.

Für Issue #18 deckt die BCL alle Anforderungen direkt ab:

- **Schlüsselmodell:** `RSAParameters` (Modulus/Exponent + private Komponenten).
- **Import/Export:** `RSA.ImportPkcs8PrivateKey`/`ExportPkcs8PrivateKey`,
  `ImportSubjectPublicKeyInfo`/`ExportSubjectPublicKeyInfo`, `ImportFromPem` sowie die PEM-Exporte;
  `X509Certificate2.GetRSAPublicKey()` für die Schlüsselentnahme aus Zertifikaten.
- **EBICS `RSAKeyValue`:** entspricht direkt `RSAParameters.Modulus`/`.Exponent`.

## Konsequenzen

- Keine zusätzliche Abhängigkeit, keine Lizenz-/Supply-Chain-Fragen, kleinere Build-Matrix.
- Konsistenz mit der bereits genutzten BCL (Tests, C14N).
- Die späteren M2-Operationen sind ebenfalls nativ abgedeckt: `RSASignaturePadding.Pss` (A006),
  `RSASignaturePadding.Pkcs1` (A004/A005, X002), `RSAEncryptionPadding.OaepSHA256` (E002),
  `Aes` und `SHA256`.
- **Risiko/Revision:** Sollte bei #21/#23 eine konkrete Interop-Lücke mit einem realen Bank-Setup
  auftreten (z. B. exotische Zertifikats- oder OAEP-Parametrisierung), wird diese ADR neu bewertet;
  bevorzugt würde dann eine eng begrenzte Abhängigkeit statt eines pauschalen Bibliothekswechsels.

## Alternativen

- **BouncyCastle:** größere Algorithmenbreite und feinere Kontrolle über Kodierungen, aber eine
  externe Abhängigkeit, die #18 (und absehbar M2) nicht benötigt — verworfen, bis ein konkreter
  Bedarf entsteht.
- **Mischbetrieb (BCL + BouncyCastle punktuell):** erhöht Komplexität ohne aktuellen Nutzen —
  verworfen; bleibt als Rückfalloption im Risiko-Abschnitt erwähnt.
