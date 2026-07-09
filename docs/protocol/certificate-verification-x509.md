# Zertifikatsverifizierung (X.509) (H005)

Die **X.509-Zertifikatsverifizierung** in `EBICO.Core` (`Crypto/`): sie prüft ein Teilnehmer- oder
Bankzertifikat gegen einen **konfigurierbaren Vertrauensanker** (Kette/Test-CA), kontrolliert
**Gültigkeit** (Zeitraum) und **Verwendungszweck** (KeyUsage passend zur EBICS-Schlüsselrolle) und
bindet das Zertifikat optional an einen bekannten Subscriber-Schlüssel. EBICS **3.0 / H005 ist
zertifikatsbasiert** — der öffentliche RSA-Schlüssel wird dort aus einem X.509-Zertifikat
(`PubKeyInfoType/X509Data`) gelesen; **H003/H004 nutzen reine RSA-Schlüssel** (Trust über den
INI-Brief-Fingerprint, [#22](public-key-fingerprint.md)). Baut auf der Schlüssel-Schicht aus
[#18](key-representation.md) auf. Issue **#23** (Milestone M2), Krypto-Bibliothek:
[ADR-0008](../adr/0008-krypto-bibliothek.md) (`System.Security.Cryptography.X509Certificates`,
kein BouncyCastle) — `X509Chain`/`X509ChainPolicy` kommen nativ aus der BCL.

> **Abgrenzung:** Diese Primitive prüft ein **einzelnes, fertig geladenes** `X509Certificate2`
> gegen Optionen. Das **XML-Auslesen** des Zertifikats aus `X509Data`, die Zuordnung
> Zertifikat↔Subscriber und die Frage, **welcher** Vertrauensanker pro Bank gilt, gehören in die
> Dispatch-/Onboarding-Schicht (M3) bzw. Suite (M7) — nicht hierher. Die Entscheidung, **ob**
> überhaupt ein Zertifikat verlangt wird, ist eine Versions-/Onboarding-Eigenschaft und über
> `CertificateRequirement` / `CertificateRequirements.For(version)` modelliert.

## Bausteine

Unter `src/EBICO.Core/Crypto/` (Namespace `EBICO.Core.Crypto`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `X509CertificateVerifier` (static) | `X509CertificateVerifier.cs` | `Verify(cert, options)` (+ Komfort-Overload): Kette bauen/prüfen, Gültigkeit, Key-Usage, optionales Key-Binding; gekapseltes `ExpectedKeyUsage`-Mapping |
| `CertificateVerificationOptions` | `CertificateVerificationOptions.cs` | konfigurierbar: `TrustAnchors`, `ExtraStore`, `TrustMode`, `RevocationMode`/`Flag`, `VerificationFlags`, `VerificationTime`, `ExpectedPurpose`, `ExpectedPublicKey` |
| `CertificateVerificationResult` | `CertificateVerificationResult.cs` | Ergebnis: `IsValid`, `Errors` (`[Flags]`), roher `ChainStatus`, `Diagnostics` |
| `CertificateVerificationError` (`[Flags]`) | `CertificateVerificationResult.cs` | Ablehnungsgründe, gemeinsam berichtbar |
| `CertificateRequirement` / `CertificateRequirements` | `CertificateRequirement.cs` | Policy „Zertifikat nötig?" je EBICS-Version (H003/H004 → `NotUsed`, H005 → `Required`) |

Wiederverwendet aus [#18](key-representation.md): `RsaKeyMaterial` (kanonischer Modulus/Exponent für
das Key-Binding), `RsaKeyImportExport.ImportPublicKeyFromCertificate`. Die `Verify`-Konvention
(sauberes Ergebnis statt Exception) folgt [#19](bank-signature.md)/[#22](public-key-fingerprint.md).

## Verfahren

`Verify` baut die Kette per `X509Chain` und leitet den Gesamtbefund aus den **gemappten** Gründen
ab — nicht aus dem `Build()`-Bool. So schlägt z. B. bei `RevocationMode.NoCheck` eine fehlende
Sperrauskunft die Prüfung **nicht** fehl.

| Schritt | Ein-/Ausgabe | BCL |
|---|---|---|
| Trust/Kette | Anker aus `TrustAnchors` (→ `CustomRootTrust`), Intermediates aus `ExtraStore` | `X509ChainPolicy.CustomTrustStore` / `.ExtraStore` |
| Zeitpunkt | `VerificationTime` (UTC-gepinnt), sonst „jetzt" | `X509ChainPolicy.VerificationTime` |
| Offline | keine AIA/CRL/OCSP-Netzcalls | `DisableCertificateDownloads = true`, `RevocationMode.NoCheck` |
| Gültigkeit | Leaf-`NotBefore`/`NotAfter` gegen Zeitpunkt (in UTC) | `X509Certificate2.NotBefore/NotAfter` |
| Key-Usage | `ExpectedPurpose` → erwartete `X509KeyUsageFlags` | `X509KeyUsageExtension.KeyUsages` |
| Key-Binding | `ExpectedPublicKey` vs. Cert-RSA (kanonisch) | `GetRSAPublicKey()` + `RsaKeyMaterial` |

**Kette/Trust:** `X509ChainStatusFlags` werden aggregiert und gemappt:
`UntrustedRoot`/`PartialChain` → `UntrustedRoot`; `NotTimeValid` → `NotTimeValid`;
`Revoked` → `Revoked`; `RevocationStatusUnknown`/`OfflineRevocation` → `RevocationStatusUnknown`;
`NotSignatureValid` → `InvalidSignature`; `InvalidBasicConstraints` → dito;
`NotValidForUsage`/`HasNotSupportedCriticalExtension` → `InvalidKeyUsage`; alles Übrige → `Other`.

**Gültigkeit:** Zusätzlich zur Ketten-Zeitprüfung verfeinert der Verifier am **Leaf** in `Expired`
(Zeitpunkt nach `NotAfter`) bzw. `NotYetValid` (Zeitpunkt vor `NotBefore`) — beide setzen auch
`NotTimeValid`.

**Verwendungszweck:** Ist `ExpectedPurpose` gesetzt, wird die KeyUsage-Extension geprüft. Fehlt die
Extension, gilt das als `InvalidKeyUsage` (strikt).

```csharp
using var ca = /* Vertrauensanker / Bank-CA */;
using var cert = /* aus X509Data geladenes Teilnehmerzertifikat */;

var result = X509CertificateVerifier.Verify(cert, TrustStore(ca), KeyPurpose.Signature);
if (!result.IsValid)
{
    // result.Errors ist ein [Flags]-Wert; result.Diagnostics liefert lesbare Texte.
}
```

### Key-Usage-Mapping

| `KeyPurpose` | erforderlich (AllOf) | eins von (AnyOf) |
|---|---|---|
| `Signature` | `DigitalSignature` | — (`NonRepudiation` erlaubt, nicht erzwungen) |
| `Authentication` | `DigitalSignature` | — |
| `Encryption` | — | `KeyEncipherment` \| `DataEncipherment` |

> **⚠️ Spec-Vorbehalt:** Das **EBICS-Zertifikatsprofil** (KeyUsage je Schlüsselrolle), die
> **strikte** Behandlung fehlender KeyUsage-Extensions, der **Revocation-Default** (`NoCheck`) und
> die **Versions-Anforderung** (`CertificateRequirements.For`) sind noch **nicht gegen die
> offiziellen EBICS-Schemas/Annexe verifiziert** (vgl. CLAUDE.md). Sie sind auf je **eine Stelle**
> gekapselt — `X509CertificateVerifier.ExpectedKeyUsage` bzw. `CertificateRequirements.For` — und
> werden dort nachgezogen, sobald die Spec vorliegt. **Extended Key Usage (EKU)** wird bewusst
> **nicht** geprüft (EBICS definiert keine Standard-EKU-OIDs für A/E/X-Schlüssel); das bleibt ein
> dokumentierter Opt-in-Erweiterungspunkt über `ChainPolicy.ApplicationPolicy`.

## Fehlerverhalten

| Bedingung | Verhalten |
|---|---|
| `certificate == null` / `options == null` / `trustAnchors == null` | `ArgumentNullException` |
| wohlgeformtes, aber ungültiges Zertifikat (untrusted/abgelaufen/falsche Usage/…) | `IsValid == false`, passende `Errors`-Bits, **kein** Throw |
| non-RSA-Zertifikat (z. B. ECDSA) | `Errors` enthält `NotRsa` (saubere Ablehnung, kein Throw) |
| mehrere Mängel gleichzeitig | alle Gründe zusammen in `Errors` (`[Flags]`) |

> **Ergebnis statt Werfen:** wie `BankSignature.Verify` liefert ein schlechtes Zertifikat eine
> saubere Ablehnung mit strukturierter Begründung; nur `null`-Argumente werfen. Aufrufer, die
> Throw-Semantik wünschen, prüfen `if (!result.IsValid) throw …` auf ihrer Ebene.

## EBICS-Versionsbezug

| Version | Schlüsselaustausch | X.509-Prüfung |
|---|---|---|
| H003 / H004 | reine RSA-Schlüssel (`RSAKeyValue`), Trust via INI-Fingerprint [#22](public-key-fingerprint.md) | **nicht anwendbar** (`CertificateRequirement.NotUsed`) — Verifier wird nicht aufgerufen |
| H005 | Zertifikat (`X509Data`) | **erforderlich** (`CertificateRequirement.Required`) — volle Ketten-/Gültigkeits-/Usage-Prüfung |

Das „Verfahren ohne Zertifikate" ist damit als Policy modelliert: Die Onboarding-Schicht fragt
`CertificateRequirements.For(version)` und ruft den Verifier nur im `Required`-Fall auf. Der Verifier
selbst behält so eine einzige Aufgabe (Zertifikate prüfen) und bekommt nie ein Zertifikat, das er
nicht prüfen sollte.

## Tests

`tests/EBICO.Tests/Crypto/X509CertificateVerifierTests.cs` und die erweiterten
`tests/EBICO.Tests/Infrastructure/TestCertificates(Tests).cs` (Tier A, CI-sicher, In-Process-CA über
`TestCertificates.CreateCertificateAuthority`/`IssueCertificate`; deterministisch via
`VerificationTime` + `NoCheck`):

- **Happy Path:** Leaf kettet zu vertrauenswürdiger Test-CA; self-signed im Trust-Store;
  Root→Intermediate→Leaf (Intermediate im `ExtraStore`); korrekte KeyUsage je `KeyPurpose`;
  `VerificationTime` im Fenster; passendes `ExpectedPublicKey`.
- **Negativfälle (je spezifisches `Errors`-Bit):** untrusted Root; abgelaufen (`Expired`);
  noch nicht gültig (`NotYetValid`); falsche KeyUsage; fehlende KeyUsage-Extension; self-signed nicht
  vertraut; `KeyMismatch`; non-RSA (ECDSA) → `NotRsa`; Mehrfachfehler (abgelaufen + untrusted).
- **Revocation:** `NoCheck` meldet kein `Revoked`/`RevocationStatusUnknown` (echter Revoked-Test
  braucht CRL/OCSP → integration-only, kein Unit-Test).
- **Null-Args:** `Verify(null, …)` / `Verify(cert, null)` / `Verify(cert, null-anchors, …)` → Throw.
- **Pure-key:** `CertificateRequirements.For` mappt H003/H004→`NotUsed`, H005→`Required`, unbekannt→Throw.

## Verwandtes

- [Schlüsselpaare & -repräsentation (A/E/X)](key-representation.md) — zugrunde liegende Schlüssel-Schicht (#18)
- [Public-Key-Fingerprints (HPB/INI/HIA)](public-key-fingerprint.md) — Trust im reinen-Schlüssel-Verfahren (#22)
- [Banktechnische Signatur A005/A006](bank-signature.md) — teilt die `Verify`-Konvention (#19)
- [ADR-0008 — Krypto-Bibliothek](../adr/0008-krypto-bibliothek.md)
