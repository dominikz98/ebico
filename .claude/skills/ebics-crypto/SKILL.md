---
name: ebics-crypto
description: >-
  Nachschlage- und Erweiterungs-Anleitung für die EBICS-Kryptografie in EBICO.Core (Namespace
  EBICO.Core.Crypto). Verwenden bei Arbeit an banktechnischer Signatur (A005/A006), Authentifikations-
  signatur (X002), hybrider Verschlüsselung (E002), Public-Key-Fingerprints, X.509-Zertifikatsverifizierung
  oder der Schlüsselrepräsentation A/E/X. Deckt die Verfahren, das registry-getriebene Padding-Mapping und
  die Versionsunterschiede H003/H004 (RSAKeyValue) vs. H005 (X.509) ab.
---

# EBICS-Krypto (A00x / X002 / E002)

Krypto-Primitive liegen in `src/EBICO.Core/Crypto` und bauen ausschließlich auf
`System.Security.Cryptography` (ADR-0008, keine Fremd-Krypto-Bibliothek). Vor jeder Änderung die
passende Protokoll-Doku unter `docs/protocol/` lesen — dort steht das verbindliche Verfahren inkl.
Spec-Vorbehalten gegen die Annexe.

## Verfahren im Überblick

- **Banktechnische Signatur A005/A006** (`docs/protocol/bank-signature.md`): Order-Hash SHA-256,
  Signieren/Verifizieren A005 = RSA **PKCS#1 v1.5**, A006 = RSA **PSS**. Padding registry-getrieben nach
  Schlüsselversion. *Hinweis:* ES/A00x-Signaturprüfung der OrderData ist serverseitig weiterhin
  zurückgestellt (Spec-Vorbehalt) — beim Erweitern beachten.
- **Authentifikationssignatur X002** (`docs/protocol/auth-signature-x002.md`): XML-DSig `AuthSignature`
  über alle `authenticate="true"`-Knoten. Reference-Digest SHA-256 + `SignatureValue` RSA-PKCS#1 v1.5,
  Dokumentkontext-C14N **inklusiv**. Serverseitig aktiv (`X002EbicsRequestVerifier`, ADR-0023).
- **Verschlüsselung E002** (`docs/protocol/encryption-e002.md`): hybrid — AES-128-CBC über die
  Auftragsdaten, RSAES-**OAEP-SHA256** über den Transaktionsschlüssel. Typ `EncryptionE002`.
- **Public-Key-Fingerprints** (`docs/protocol/public-key-fingerprint.md`): SHA-256 über Exponent+Modulus,
  Darstellung für INI-Brief und HPB-Antwort; client-gesendete Hashes **konstantzeitig** verifizieren
  (`PublicKeyFingerprint.Verify`).
- **X.509-Verifizierung** (`docs/protocol/certificate-verification-x509.md`): Kette/Vertrauensanker
  (konfigurierbar, Test-CA), Gültigkeit, KeyUsage je Schlüsselrolle; reine-Schlüssel-Verfahren
  (H003/H004) als Policy (`CertificateRequirement`).

## Querschnittliche Regeln

- **Registry-getriebenes Padding-Mapping:** das Padding (v1.5/PSS/OAEP) hängt an der Schlüsselversion
  (`KeyVersion`), nicht am Aufrufort — Mapping zentral halten, nicht duplizieren.
- **Versionsrepräsentation** (`docs/protocol/key-representation.md`): H003/H004 transportieren Schlüssel als
  `RSAKeyValue`, H005 als X.509. Schlüsselrollen A (Signatur) / E (Enc, E002) / X (Auth, X002).
  RSA-Container über `RsaKeyMaterial` (RSA-2048 als praktische Untergrenze in Tests).
- **Determinismus/C14N:** serialisierte Ausgabe muss stabil sein (`docs/protocol/serialization-c14n.md`),
  da Signatur/Digest darauf beruhen.

## Definition of Done

Testvektoren + Sample-XML statt Selbstkonsistenz (`tests/EBICO.Tests/Crypto`), Doku aktualisieren,
ggf. ADR. Ablauf: Skill `ebics-feature-workflow`.

## Quellen

- Code: `src/EBICO.Core/Crypto`, `src/EBICO.Core/ReturnCodes` (`EbicsReturnCode`/`EbicsReturnCodes`).
- Doku: `docs/protocol/bank-signature.md`, `docs/protocol/auth-signature-x002.md`,
  `docs/protocol/encryption-e002.md`, `docs/protocol/public-key-fingerprint.md`,
  `docs/protocol/certificate-verification-x509.md`, `docs/protocol/key-representation.md`,
  `docs/protocol/serialization-c14n.md`. ADR: 0008 (Krypto-Bibliothek), 0023 (X002-Verifikation).
