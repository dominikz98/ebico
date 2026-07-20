# Suite: Schlüssel-/Zertifikats-Ansicht

> Umsetzung von **Issue #55** (Milestone M7 — Suite). Baut auf dem UI-Grundgerüst
> ([#52](ui-shell.md)) und den Krypto-Bausteinen aus M2 auf: Public-Key-Fingerprints
> ([#22](../protocol/public-key-fingerprint.md)), Schlüsselrepräsentation
> ([#18](../protocol/key-representation.md)) und Zertifikatsverifizierung
> ([#23](../protocol/certificate-verification-x509.md)).

## Zweck

Die Seite `/schluessel` ist die Schlüssel-/Zertifikats-Ansicht der Inspektor-UI. Sie macht die
öffentlichen Schlüssel der Bank und Teilnehmer mit ihren SHA-256-Fingerprints sichtbar und
stellt zwei Werkzeuge bereit: den **INI-Brief-Vergleich** (der manuelle Fingerprint-Abgleich, den
eine Bank bei INI-Eingang durchführt) und **Test-CA/Schlüssel-Werkzeuge** zum Erzeugen von
RSA-Test-Schlüsseln und self-signed Test-Zertifikaten.

Sämtliche Krypto-Operationen laufen über die vorhandenen Primitive in `EBICO.Core.Crypto`; die
Suite referenziert nur `EBICO.Core`.

## Render-Modus

Die Seite selbst ist **Static SSR** (reine Anzeige, lädt die Schlüssel in `OnInitializedAsync`).
Die beiden Werkzeuge sind **interaktive Inseln**: der Render-Modus wird am Einbettungsort gesetzt
(`<IniLetterComparisonTool @rendermode="InteractiveServer" />`), nicht in den Komponenten selbst —
gemäß [ADR-0009](../adr/0009-blazor-render-mode.md) („Interaktivität pro Komponente"). Es sind die
ersten interaktiven Komponenten der Suite.

## Aufbau

| Abschnitt | Inhalt | Datenquelle |
| --- | --- | --- |
| Bekannte Schlüssel & Fingerprints | Tabelle Inhaber / Zweck (A/E/X) / Version / Fingerprint (SHA-256, INI-Brief-Format) | `IEmulatorStateProvider.GetKeysAsync` |
| Schlüsselversionen (Referenz) | Katalog A004/A005/A006, E001/E002, X001/X002 mit Legacy-Flag, Padding und erlaubten EBICS-Versionen | `EBICO.Core.Crypto.KeyVersions` |
| INI-Brief-Vergleich | interaktives Werkzeug (Insel) | `PublicKeyFingerprint.Verify` |
| Test-CA & Schlüssel-Werkzeuge | interaktives Werkzeug (Insel) | `RsaKeyMaterial.Generate`, `SelfSignedCertificateFactory`, `X509CertificateVerifier` |

Datenanbindung wie bei Dashboard/Stammdaten über das Read-Model `IEmulatorStateProvider`, hier um
`GetKeysAsync()` erweitert. Die Schlüssel kommen aus den serverseitigen Key-Stores: die
Teilnehmer-Schlüssel (A/E/X) aus `IServerKeyStore` (wie sie INI/HIA beim Onboarding ablegen) und das
Bank-Keypaar (X/E) aus `IServerBankKeyStore` (wie es HPB zurückgibt) — in-process gebunden gemäß
[ADR-0009](../adr/0009-blazor-render-mode.md). Da die Suite keine EBICS-Pipeline betreibt, füllt der
`KeyStoreSeeder` diese Stores beim Start aus deterministischem Beispielmaterial (`KeyStoreSeedData`,
fest eingebettete 2048-Bit-Public-Keys); die Fingerprints berechnet `KeyViewFactory` per
`PublicKeyFingerprint.Compute` vor. Bank-Schlüssel werden nur für die geseedeten Hosts gelesen, damit
das Rendern der Seite kein frisches (nicht-reproduzierbares) Bank-Keypaar erzeugt.

Das neue DTO:

```csharp
public sealed record KeyView
{
    public required string OwnerLabel { get; init; }      // "Teilnehmer PARTNER01 / USER0001"
    public required KeyPurpose Purpose { get; init; }      // Signature / Encryption / Authentication
    public required string KeyVersion { get; init; }       // "A006"
    public required RsaKeyMaterial PublicKey { get; init; }
    public required string FingerprintText { get; init; }  // ToLetterFormat(Compute(PublicKey))
}
```

## Fingerprints & INI-Brief-Vergleich

Der Fingerprint wird für die Anzeige mit `PublicKeyFingerprint.ToLetterFormat` als gruppiertes
Großbuchstaben-Hex (acht Bytes je Zeile) gerendert — genau die Darstellung des INI-Briefs.

Der INI-Brief-Vergleich wählt einen bekannten Schlüssel (oder nimmt einen eingefügten Public Key
im PEM-Format via `RsaKeyImportExport.ImportFromPem`), liest den aus dem Brief abgetippten
Fingerprint (Hex, Leerzeichen/Zeilenumbrüche erlaubt — geparst durch `FingerprintFormat.TryParseHex`)
und prüft ihn **konstantzeitig** mit `PublicKeyFingerprint.Verify(key, expectedDigest)`. Ergebnis:
Übereinstimmung, Abweichung (mit Anzeige des tatsächlichen Fingerprints) oder freundliche
Fehlermeldung bei ungültiger Eingabe — nie eine Exception.

## Test-CA & Schlüssel-Werkzeuge

- **Schlüssel erzeugen:** `RsaKeyMaterial.Generate()` (2048 Bit) → zeigt Fingerprint und
  Public-Key-PEM (`RsaKeyImportExport.ExportPublicKeyPem`).
- **Test-Zertifikat erzeugen:** `SelfSignedCertificateFactory.Create(key, purpose, subject, …)` →
  Verifikation mit `X509CertificateVerifier.Verify(cert, { cert }, purpose)` (Default
  `CustomRootTrust` + `NoCheck`, d. h. das self-signed Zertifikat gilt als eigener Vertrauensanker)
  → zeigt Verdikt, Subject, Gültigkeit und Thumbprint.
- **Download:** Public-Key-, Private-Key- (`ExportPkcs8Pem`) und Zertifikat-PEM
  (`ExportCertificatePem`) werden per JS-Interop (`wwwroot/download.js`, Funktion `ebicoDownload`)
  als Datei heruntergeladen.

> **⚠️ Nur für Tests:** die erzeugten Schlüssel/Zertifikate sind Testmaterial für die
> Onboarding-Flows, kein Produktivschlüsselmaterial.

## EBICS-Versionsbezug

| Zweck | Schlüsselversionen | Zertifikate |
| --- | --- | --- |
| Signatur (A) | A004 (legacy, H003/H004), A005 (alle), A006 (nur H005) | H005: `X509Data` statt `RSAKeyValue` |
| Verschlüsselung (E) | E001 (legacy, H003/H004), E002 (alle) | — |
| Authentifikation (X) | X001 (legacy, H003/H004), X002 (alle) | — |

Der Fingerprint ist versionsagnostisch (er sieht nur `RsaKeyMaterial`); die Zertifikats-Werkzeuge
zielen auf H005 (EBICS 3.0), wo Schlüssel als Zertifikate ausgetauscht werden.

## Tests

`tests/EBICO.Tests/Suite/` deckt ab:

- `SampleEmulatorStateProviderTests` — `GetKeysAsync` liefert die Beispielschlüssel; die
  Fingerprint-Texte stimmen mit der Core-Berechnung überein; stabil über Aufrufe.
- `KeyStoreSeederTests` — der `KeyStoreSeeder` legt die Teilnehmer-Schlüssel (A006/E002/X002) im
  `IServerKeyStore` und das Bank-Keypaar (X002/E002, public-only) im `IServerBankKeyStore` ab und ist
  idempotent.
- `EmulatorStateProviderTests` — `GetKeysAsync` liest genau die geseedeten Schlüssel aus den Stores
  (fünf Einträge, erwartete Inhaber/Versionen, Fingerprint == Core-Berechnung); ein Teilnehmer ohne
  hinterlegte Schlüssel bzw. eine nicht geseedete Bank erzeugt keinen Eintrag.
- `FingerprintFormatTests` — Parsen von Hex mit Whitespace/Groß-Klein, Round-Trip gegen
  `ToLetterFormat`, Ablehnung ungültiger Eingaben.
- `SchluesselPageTests` (bUnit) — die Seite rendert die Schlüssel-Fingerprints und den
  KeyVersions-Katalog.
- `IniLetterComparisonToolTests` (bUnit) — passender Fingerprint → Erfolg, abweichender → Fehler,
  ungültiger → Warnung.
- `TestKeyToolTests` (bUnit) — Schlüssel/Zertifikat erzeugen, gültig-Verdikt, Download über
  JS-Interop.

## Verwandtes

- [UI-Grundgerüst & Navigation](ui-shell.md)
- [Public-Key-Fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md)
- [Schlüsselpaare & -repräsentation (A/E/X)](../protocol/key-representation.md)
- [Zertifikatsverifizierung (X.509)](../protocol/certificate-verification-x509.md)
