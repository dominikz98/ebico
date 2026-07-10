# Server: HIA — Senden der Auth- & Enc-Schlüssel (X002/E002)

> Umsetzung von **Issue #27** (Milestone M3 — Server: Key Management). Diese Seite
> beschreibt den zweiten fachlichen **Order-Handler** des Emulators: den Empfang des
> öffentlichen **Authentifizierungsschlüssels** (X00x) und **Verschlüsselungsschlüssels**
> (E00x) eines Teilnehmers per **HIA**, das serverseitige **Speichern** beider Schlüssel
> und den Lebenszyklus-Übergang **`Initialized → Ready`**.
>
> Bewusst **enthalten**: OrderType-`HIA`-Verarbeitung für H003/H004/H005, Extraktion und
> Speicherung des X00x- und E00x-Schlüssels, Antwort als `ebicsKeyManagementResponse`,
> Returncodes für die Fehlerfälle (INI noch nicht gelaufen, unbekannter Teilnehmer,
> bereits abgeschlossen, defekte Order-Data).
> Bewusst **noch nicht**: HPB (#28), Antwort-Signatur (X002, M4), Persistenz des
> Schlüssel-Stores (In-Memory bleibt Default), Zertifikatsketten-Prüfung bei H005 (M8),
> vollständiger Returncode-Katalog (#36/M4), freie INI/HIA-Reihenfolge (siehe Spec-Vorbehalte).

## Zweck

HIA ist der zweite Schritt der Teilnehmer-Initialisierung (nach [INI](ini.md)): der
Client sendet einen **ungesicherten** `ebicsUnsecuredRequest`, dessen Order-Data das
selbstbeschreibende `HIARequestOrderData`-Dokument mit dem öffentlichen
Authentifizierungsschlüssel (Version X001/X002 — „X00x") und dem
Verschlüsselungsschlüssel (Version E001/E002 — „E00x") trägt. Der Server nimmt beide
Schlüssel entgegen, legt sie ab und markiert den Teilnehmer als `Ready`.

Der Client-Gegenpart (Schlüsselerzeugung, HIA senden) ist im Connector umgesetzt
(siehe [Onboarding-Flows](../connector/onboarding.md)) und liefert genau die Order-Data,
die dieser Handler konsumiert.

## Ablauf

Die Pipeline (`EbicsRequestPipeline`) erkennt den ungesicherten Request, zieht den
OrderType `HIA` aus dem Header und leitet an den versionspassenden Handler weiter. Der
versionsagnostische Ablauf liegt in `HiaOrderHandlerBase`, die versionsspezifische
Schlüssel-Extraktion in `H003`/`H004`/`H005HiaOrderHandler`:

| Schritt | Aktion |
| --- | --- |
| 1. Extraktion | `Body/DataTransfer/OrderData` (base64 vom Binding dekodiert) → `EbicsCompression.Decompress` → `EbicsXmlSerializer.Deserialize<HiaRequestOrderData>` |
| 2. Schlüssel | Je Schlüssel — H003/H004: `PubKeyValue/RSAKeyValue` (Modulus/Exponent) → `RsaKeyImportExport.ImportRsaKeyValue`. H005: `X509Data` → `RsaKeyImportExport.ImportPublicKeyFromCertificate` |
| 3. Versionsprüfung | `AuthenticationVersion` muss eine X00x-, `EncryptionVersion` eine E00x-Version und beide für die Protokollversion zulässig sein (`KeyVersions.EnsurePermitted`) |
| 4. Teilnehmer | `IMasterDataManager.GetSubscriberAsync` — muss existieren und im Zustand `Initialized` sein (INI zuvor gelaufen) |
| 5. Speichern | beide öffentlichen Schlüssel → `IServerKeyStore.StoreAsync` (gekeyt auf Teilnehmer × `KeyPurpose.Authentication` bzw. `KeyPurpose.Encryption`) |
| 6. Status | `IMasterDataManager.TransitionSubscriberAsync(…, Ready)` |
| 7. Antwort | `ebicsKeyManagementResponse` mit `000000`/`000000` (`EbicsResponseFactory.BuildKeyManagementResponse`) |

Beispiel — HIA-Order-Data (H004, gekürzt), vor Kompression/Base64:

```xml
<HIARequestOrderData xmlns="urn:org:ebics:H004" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
  <AuthenticationPubKeyInfo>
    <PubKeyValue>
      <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    </PubKeyValue>
    <AuthenticationVersion>X002</AuthenticationVersion>
  </AuthenticationPubKeyInfo>
  <EncryptionPubKeyInfo>
    <PubKeyValue>
      <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    </PubKeyValue>
    <EncryptionVersion>E002</EncryptionVersion>
  </EncryptionPubKeyInfo>
  <PartnerID>PARTNER01</PartnerID>
  <UserID>USER01</UserID>
</HIARequestOrderData>
```

Erfolgsantwort (H004, gekürzt):

```xml
<ebicsKeyManagementResponse xmlns="urn:org:ebics:H004" Version="H004">
  <header authenticate="true">
    <static/>
    <mutable><ReturnCode>000000</ReturnCode><ReportText>EBICS_OK</ReportText></mutable>
  </header>
  <body><ReturnCode>000000</ReturnCode></body>
</ebicsKeyManagementResponse>
```

## Schlüssel-Store

Der Server hält empfangene öffentliche Schlüssel im `IServerKeyStore`
(Default `InMemoryServerKeyStore`, via `TryAddSingleton` überschreibbar). Er ist auf
(`HostId`, `PartnerId`, `UserId`) × `KeyPurpose` gekeyt und speichert ausschließlich den
**öffentlichen** Schlüssel plus die EBICS-Schlüsselversion (`StoredPublicKey`). HIA legt
zwei Einträge ab: den Authentifizierungsschlüssel (`X00x`, `KeyPurpose.Authentication`)
und den Verschlüsselungsschlüssel (`E00x`, `KeyPurpose.Encryption`). Sie stehen
purpose-isoliert neben dem bereits per [INI](ini.md) abgelegten Signaturschlüssel
(`A00x`). Das Domänen-Aggregat `Subscriber` bleibt bewusst schlüsselfrei (siehe
[Stammdaten](master-data.md)).

## Returncodes & Fehlerfälle

Wie beim gesamten `/ebics`-Endpoint werden Protokoll-/Businessfehler mit **HTTP 200** und
einem Returncode im Envelope beantwortet (siehe [host.md](host.md)); der fachliche Code
steht in `body/ReturnCode`.

| Situation | Returncode |
| --- | --- |
| HIA angenommen | `000000` EBICS_OK |
| Teilnehmer unbekannt, **noch nicht** `Initialized` (INI fehlt) **oder** nicht mehr `Initialized` (bereits `Ready`/`Suspended`) | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Order-Data nicht entpack-/deserialisierbar, unbrauchbares Schlüsselmaterial oder falsche/unzulässige Auth-/Enc-Version | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

HIA wird also nur im Zustand `Initialized` angenommen; ein erneutes HIA (Teilnehmer schon
`Ready`) wird **strikt abgelehnt** — das deckt sich mit den erlaubten Übergängen der
Domäne (`Initialized → Ready`).

### ⚠️ Spec-Vorbehalte

- **Reihenfolge INI vor HIA wird erzwungen.** Da das Domänenmodell nur
  `New → Initialized → Ready` kennt (kein Zwischenzustand für „HIA erledigt, INI fehlt"),
  akzeptiert HIA nur einen `Initialized`-Teilnehmer und setzt damit INI voraus. Die
  EBICS-Spezifikation erlaubt INI/HIA grundsätzlich in beliebiger Reihenfolge; diese
  Vereinfachung ist gegen den offiziellen Ablauf zu verifizieren.
- **`Ready` ohne separaten Aktivierungsschritt.** HIA schaltet direkt auf `Ready`. In der
  Praxis wird ein Teilnehmer erst nach Abgleich der INI-/HIA-Briefe und expliziter
  Freischaltung durch die Bank aktiv; der Emulator nimmt diesen Schritt (mangels
  Operator/Brief-Workflow) implizit vorweg.
- Die konkreten Codes (`091002` für Zustandsfehler, `090004` für Order-Data-Format) sind
  gegen den offiziellen EBICS-Annex 1 zu verifizieren; der vollständige, zentrale
  Returncode-Katalog kommt mit **#36 (M4)**.
- Die Antwort ist **unsigniert** — die Antwort-Authentifikationssignatur (X002) ist **M4**;
  strikte Clients könnten unsignierte Antworten ablehnen (konsistent mit `EbicsResponseFactory`).
- **H005:** aus den übermittelten Zertifikaten wird nur der öffentliche Schlüssel entnommen
  und gespeichert; eine Zertifikatsketten-/Selbstsignaturprüfung ist ein Conformance-Thema (**M8**).
- `OrderAttribute`/`SecurityMedium` werden nicht erzwungen (unverifiziert, wie im Connector).

## EBICS-Versionsbezug

| Version | Order-Data | Schlüsseltransport | OrderType-Feld |
| --- | --- | --- | --- |
| H003 / H004 | `H00x.HIARequestOrderData` | `RSAKeyValue` (Modulus/Exponent) je Schlüssel | `OrderType` |
| H005 | `H005.HIARequestOrderData` | `X509Data` (Zertifikat) je Schlüssel | `AdminOrderType` |

Erlaubte Versionen (via `KeyVersions`): Authentifizierung **X001** (nur H003/H004),
**X002** (alle); Verschlüsselung **E001** (nur H003/H004), **E002** (alle). Eine für die
Protokollversion unzulässige Version (z. B. E001 auf H005) oder eine zweckfremde Version
(z. B. A005 als `AuthenticationVersion`) wird mit `090004` abgelehnt.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Request-XML aus committeten
Core-Bindings, keine proprietären Fixtures):

- `HiaOrderHandlerTests` — End-to-End über `EbicsRequestPipeline`, `[Theory]` über H003/H004/H005:
  Happy Path (Antwort `ebicsKeyManagementResponse` `000000`, Teilnehmer `Initialized→Ready`,
  **beide** Schlüssel im `IServerKeyStore` mit passendem Modulus/Version) plus Negativfälle:
  Teilnehmer noch `New` (INI fehlt), unbekannter Teilnehmer und bereits `Ready` (`091002`),
  undekodierbare Order-Data (`090004`), für die Protokollversion unzulässige (E001/H005) bzw.
  zweckfremde (A005 als AuthenticationVersion) Version (`090004`).
- `InMemoryServerKeyStoreTests` — Store/Get/Contains, Purpose-Isolation, Overwrite, Teilnehmer-Isolation.

## Verwandte Doku

- [INI — Senden der Signaturschlüssel (A00x)](ini.md) — der vorausgehende Onboarding-Schritt
- [Hostable Server-Grundgerüst](host.md) — Host, Pipeline, Returncodes, Response-Factory
- [Stammdatenverwaltung](master-data.md) — Teilnehmer-Lebenszyklus, `IMasterDataManager`, Store
- [Onboarding-Flows INI / HIA / HPB](../connector/onboarding.md) — der Client-Gegenpart
- [Schlüsselpaare & -repräsentation (A/E/X)](../protocol/key-representation.md) — Schlüsselversionen, RSAKeyValue/X.509-Import
- [Public-Key-Fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md) — HIA-Brief-Abgleich
