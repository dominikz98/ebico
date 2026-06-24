# Schlüsselpaare & -repräsentation (A/E/X) (H003/H004/H005)

Die erste Krypto-Schicht in `EBICO.Core` (`Crypto/`): typsichere Schlüsselversionen
(A00x/E002/X002), ein RSA-Schlüsselcontainer und Import/Export über PKCS#8, X.509/SPKI,
PEM und die EBICS-`RSAKeyValue`-Darstellung. Issue **#18** (Milestone M2),
Krypto-Bibliothek: [ADR-0008](../adr/0008-krypto-bibliothek.md)
(`System.Security.Cryptography`, kein BouncyCastle).

> **Abgrenzung:** Bewusst nur **Repräsentation, Import/Export und Versions-Mapping**.
> Signieren/Verifizieren (A005/A006 #19, X002 #20), Verschlüsselung (E002 #21),
> Hashing/Fingerprints (HPB/INI/HIA #22) und X.509-Kettenprüfung (#23) gehören **nicht**
> hierher. Die `RsaPaddingScheme`-Angaben sind reine Metadaten (Absicht), es wird in dieser
> Schicht keine Krypto-Operation ausgeführt. Auch das Mapping auf die generierten
> [Bindings](xsd-bindings.md) (`PubKeyInfoType` u. a.) ist auf die INI/HIA/HPB-Order-Data-Issues
> verschoben; #18 liefert dafür nur `ExportRsaKeyValue` (Modulus/Exponent).

## Bausteine

Alle unter `src/EBICO.Core/Crypto/` (Namespace `EBICO.Core.Crypto`):

| Baustein | Ort | Aufgabe |
|---|---|---|
| `KeyPurpose` (Enum) + `KeyPurposeExtensions` | `KeyPurpose.cs` | Schlüsselrolle Signatur/Enc/Auth ↔ Versionsbuchstabe `A`/`E`/`X` |
| `KeyVersion` (`readonly record struct`) + `RsaPaddingScheme` (Enum) | `KeyVersion.cs` | validierter 4-Zeichen-Code (`[AEX]\d{3}`); Padding-Schema als Metadatum |
| `KeyVersionInfo` | `KeyVersionInfo.cs` | unveränderliche Metadaten je Version (Purpose, Legacy, Padding-Absicht, erlaubte EBICS-Versionen) |
| `KeyVersions` | `KeyVersions.cs` | Registry/Single Source of Truth + Versions-Mapping je EBICS-Version |
| `RsaKeyMaterial` | `RsaKeyMaterial.cs` | unveränderlicher RSA-Container (öffentlich, optional privat); kanonische Modulus-/Exponent-Form |
| `RsaKeyImportExport` | `RsaKeyImportExport.cs` | PKCS#8 / SPKI / X.509 / PEM / `RSAKeyValue` Import & Export |
| `EbicsCryptoException` (+ abgeleitete) | `CryptoExceptions.cs` | Fehler der Krypto-Schicht |

## Schlüsselrollen & -versionen

EBICS unterscheidet drei RSA-Schlüsselrollen, kenntlich am führenden Versionsbuchstaben:

| Rolle (`KeyPurpose`) | Buchstabe | Versionen | Bedeutung |
|---|---|---|---|
| `Signature` | `A` | A004/A005/A006 | banktechnische (autorisierende) Signatur |
| `Encryption` | `E` | E001/E002 | Verschlüsselung (Transaktionsschlüssel/Auftragsdaten) |
| `Authentication` | `X` | X001/X002 | Authentifikation/Identifikation von Requests |

> **Hinweis:** Der Signatur-Versionsbuchstabe `A` hat **nichts** mit `SignatureClass.A`
> (Erstunterschrift, siehe [Domänenmodell](domain-model.md)) zu tun — gleicher Buchstabe,
> anderes Konzept.

`KeyVersion.Create` prüft nur die **Form** (Buchstabe A/E/X + drei Ziffern). Ein
wohlgeformter, aber unbekannter Code (`"A999"`) wird akzeptiert, löst aber über
`KeyVersions.TryGet` nicht auf — die Kenntnis bekannter Versionen liegt in der Registry.

```csharp
var v = KeyVersion.Create("A005");      // v.Purpose == KeyPurpose.Signature
KeyVersion.Create("a005");              // InvalidKeyVersionException (Kleinbuchstabe)
KeyVersion.TryCreate("E002", out var e);// nicht-werfende Variante
default(KeyVersion).Value;              // null — struct-Caveat (vgl. ADR-0007)
```

## Versions-Mapping je EBICS-Version

`KeyVersions` ist die einzige Stelle, die weiß, welche Schlüsselversion mit welcher
EBICS-Protokollversion erlaubt ist (analog zur `EbicsVersions`-Registry).

| Code | Rolle | Legacy | Padding (Metadatum) | erlaubt in |
|---|---|---|---|---|
| A004 | Signatur | ja | Pkcs1V15 | H003, H004 |
| A005 | Signatur | nein | Pkcs1V15 | H003, H004, H005 |
| A006 | Signatur | nein | Pss | H005 |
| E001 | Enc | ja | Pkcs1V15Encryption | H003, H004 |
| E002 | Enc | nein | Oaep | H003, H004, H005 |
| X001 | Auth | ja | Pkcs1V15 | H003, H004 |
| X002 | Auth | nein | Pkcs1V15 | H003, H004, H005 |

```csharp
KeyVersions.IsPermitted(KeyVersion.Create("A006"), EbicsVersion.H003);   // false
KeyVersions.EnsurePermitted(KeyVersion.Create("A006"), EbicsVersion.H005); // ok
KeyVersions.Default(KeyPurpose.Signature, EbicsVersion.H005).Code;        // "A005" (A006 ist Opt-in)
KeyVersions.PermittedFor(KeyPurpose.Signature, EbicsVersion.H005);        // A005, A006
```

> **⚠️ Spec-Vorbehalt:** Diese Tabelle (Legacy-Versionen in 3.0 zurückgezogen, A006 erst ab
> H005, Default A005) folgt der gängigen Lesart und ist **noch nicht gegen die offiziellen
> EBICS-XSDs/Annexe verifiziert** (vgl. CLAUDE.md). Sie wird bei Vorliegen der Schemas an
> dieser einen Stelle (`KeyVersions`) nachgezogen.

## Schlüsselmaterial: `RsaKeyMaterial`

Unveränderlicher Container; speichert geklonte `RSAParameters` statt einer lebenden
`RSA`-Instanz (kein `IDisposable`, kein Use-after-Dispose). Für eine Operation liefert
`CreateRsa()` eine frische `RSA` (Aufrufer entsorgt sie). `Modulus`/`Exponent` werden in
**EBICS-kanonischer Form** (vorzeichenloses Big-Endian, ohne führende Null) ausgegeben, damit
spätere Fingerprints (#22) und die Order-Data-Schicht dieselben Bytes sehen.

- `FromPublicKey(RSA)` / `FromKeyPair(RSA)` / `FromModulusExponent(mod, exp)`
- `HasPrivateKey`, `KeySizeBits`, `ToPublicOnly()`
- **Mindestschlüsselgröße:** `MinKeySizeBits = 2048` (EBICS erlaubt 1536–4096; revidierbare
  Policy). Kleinere Schlüssel werden beim Import mit `KeyMaterialException` abgelehnt.

## Import / Export — `RsaKeyImportExport`

Dünne Wrapper um die BCL ([ADR-0008](../adr/0008-krypto-bibliothek.md)); BCL-`CryptographicException`
wird einheitlich in `KeyMaterialException` übersetzt.

| Format | Import | Export |
|---|---|---|
| PKCS#8 (privat, DER) | `ImportPkcs8` | `ExportPkcs8` |
| SubjectPublicKeyInfo (öffentlich, DER) | `ImportSubjectPublicKeyInfo` | `ExportSubjectPublicKeyInfo` |
| X.509-Zertifikat | `ImportPublicKeyFromCertificate` (nur Schlüssel, **keine** Kettenprüfung) | — |
| PEM | `ImportFromPem` (privat/öffentlich autom.) | `ExportPublicKeyPem`, `ExportPkcs8Pem` |
| EBICS `RSAKeyValue` (Modulus/Exponent) | `ImportRsaKeyValue` | `ExportRsaKeyValue` |

```csharp
var material = RsaKeyImportExport.ImportPkcs8(pkcs8Der);   // HasPrivateKey == true
var (modulus, exponent) = RsaKeyImportExport.ExportRsaKeyValue(material);
RsaKeyImportExport.ExportPkcs8(material.ToPublicOnly());   // KeyMaterialException (kein privater Schlüssel)
```

## EBICS-Versionsbezug

Schlüsselrollen (A/E/X) und die RSA-Basis sind über H003/H004/H005 identisch; nur die
**zulässigen Versionen** unterscheiden sich (siehe Tabelle oben) und liegen zentral in
`KeyVersions`. Die `RSAKeyValue`-Bytes (Modulus/Exponent) entsprechen der gemeinsamen,
versionsunabhängigen Binding `XmlDsig.RsaKeyValueType`.

## Tests

`tests/EBICO.Tests/Crypto/` (Tier A, CI-sicher, ohne proprietäre Beispiele):

- `KeyPurposeTests` — Buchstaben-Mapping A/E/X, Ablehnung unbekannter Buchstaben.
- `KeyVersionTests` — Formvalidierung, Purpose-Ableitung, wohlgeformt-aber-unbekannt, `default`-Caveat.
- `KeyVersionsTests` — Registry-Inhalt/-Reihenfolge, `Get`/`TryGet`, Permission-Tabelle,
  `EnsurePermitted`/`PermittedFor`/`Default`, Legacy-/Padding-Metadaten.
- `RsaKeyMaterialTests` — öffentlich/privat, Mindestgröße, kanonische Modulus-Form, defensives Kopieren.
- `RsaKeyImportExportTests` — Round-Trip-Treue (PKCS#8/SPKI/PEM/`RSAKeyValue`), Cross-Format,
  Zertifikatsentnahme, Fehlerfälle (malformed/EC-Zertifikat/zu klein) **und** ein fixer,
  extern erzeugter Known-Answer-Vektor zur Kanonisierungs-Absicherung.

## Verwandtes

- [Banktechnische Signatur A005/A006](bank-signature.md) — die erste Krypto-Operation, die auf dieser Schicht aufbaut (#19)
- [ADR-0008 — Krypto-Bibliothek](../adr/0008-krypto-bibliothek.md)
- [ADR-0007 — Domänen-Value-Objects als `readonly record struct`](../adr/0007-domaenen-value-objects-record-struct.md) — Muster für `KeyVersion`
- [Versions-Dispatch](version-dispatch.md) — die `EbicsVersion`-Registry, auf die `KeyVersions` Bezug nimmt
- [XSD-Bindings](xsd-bindings.md) — `RsaKeyValueType` und die (später anzubindenden) `PubKeyInfoType`-Typen
