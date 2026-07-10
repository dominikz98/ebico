# Server: INI — Senden der Signaturschlüssel (A00x)

> Umsetzung von **Issue #26** (Milestone M3 — Server: Key Management). Diese Seite
> beschreibt den ersten fachlichen **Order-Handler** des Emulators: den Empfang des
> öffentlichen bankfachlichen **Signaturschlüssels** (A00x) eines Teilnehmers per **INI**,
> das serverseitige **Speichern** des Schlüssels und den Lebenszyklus-Übergang
> **`New → Initialized`**.
>
> Bewusst **enthalten**: OrderType-`INI`-Verarbeitung für H003/H004/H005, Extraktion und
> Speicherung des A00x-Schlüssels, Antwort als `ebicsKeyManagementResponse`, Returncodes
> für die Fehlerfälle (bereits initialisiert, unbekannter Teilnehmer, defekte Order-Data).
> Bewusst **noch nicht**: HIA/HPB (#27/#28), Antwort-Signatur (X002, M4), Persistenz des
> Schlüssel-Stores (In-Memory bleibt Default), Zertifikatsketten-Prüfung bei H005 (M8),
> vollständiger Returncode-Katalog (#36/M4).

## Zweck

INI ist der erste Schritt der Teilnehmer-Initialisierung: der Client sendet einen
**ungesicherten** `ebicsUnsecuredRequest`, dessen Order-Data das selbstbeschreibende
`SignaturePubKeyOrderData`-Dokument mit dem öffentlichen Signaturschlüssel (Version
A004/A005/A006 — „A00x") trägt. Der Server nimmt den Schlüssel entgegen, legt ihn ab
und markiert den Teilnehmer als `Initialized`. Das Grundgerüst (#25, siehe
[host.md](host.md)) hatte hierfür die Pipeline-Erweiterungspunkte vorbereitet; #26
füllt den ersten davon.

Der Client-Gegenpart (Schlüsselerzeugung, INI senden) ist im Connector umgesetzt
(siehe [Onboarding-Flows](../connector/onboarding.md)) und liefert genau die Order-Data,
die dieser Handler konsumiert.

## Ablauf

Die Pipeline (`EbicsRequestPipeline`) erkennt den ungesicherten Request, zieht den
OrderType `INI` aus dem Header und leitet an den versionspassenden Handler weiter. Der
versionsagnostische Ablauf liegt in `IniOrderHandlerBase`, die versionsspezifische
Schlüssel-Extraktion in `H003`/`H004`/`H005IniOrderHandler`:

| Schritt | Aktion |
| --- | --- |
| 1. Extraktion | `Body/DataTransfer/OrderData` (base64 vom Binding dekodiert) → `EbicsCompression.Decompress` → `EbicsXmlSerializer.Deserialize<SignaturePubKeyOrderData>` |
| 2. Schlüssel | H003/H004: `PubKeyValue/RSAKeyValue` (Modulus/Exponent) → `RsaKeyImportExport.ImportRsaKeyValue`. H005: `X509Data` → `RsaKeyImportExport.ImportPublicKeyFromCertificate` |
| 3. Versionsprüfung | `SignatureVersion` muss eine A00x-Version und für die Protokollversion zulässig sein (`KeyVersions.EnsurePermitted`) |
| 4. Teilnehmer | `IMasterDataManager.GetSubscriberAsync` — muss existieren und im Zustand `New` sein |
| 5. Speichern | öffentlicher Schlüssel → `IServerKeyStore.StoreAsync` (gekeyt auf Teilnehmer × `KeyPurpose.Signature`) |
| 6. Status | `IMasterDataManager.TransitionSubscriberAsync(…, Initialized)` |
| 7. Antwort | `ebicsKeyManagementResponse` mit `000000`/`000000` (`EbicsResponseFactory.BuildKeyManagementResponse`) |

Beispiel — INI-Order-Data (H004, `S001`, gekürzt), vor Kompression/Base64:

```xml
<SignaturePubKeyOrderData xmlns="http://www.ebics.org/S001" xmlns:ds="http://www.w3.org/2000/09/xmldsig#">
  <SignaturePubKeyInfo>
    <ds:RSAKeyValue><ds:Modulus>…</ds:Modulus><ds:Exponent>AQAB</ds:Exponent></ds:RSAKeyValue>
    <SignatureVersion>A005</SignatureVersion>
  </SignaturePubKeyInfo>
  <PartnerID>PARTNER01</PartnerID>
  <UserID>USER01</UserID>
</SignaturePubKeyOrderData>
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

Der Server hält empfangene öffentliche Schlüssel im neuen `IServerKeyStore`
(Default `InMemoryServerKeyStore`, via `TryAddSingleton` überschreibbar). Er ist auf
(`HostId`, `PartnerId`, `UserId`) × `KeyPurpose` gekeyt und speichert ausschließlich den
**öffentlichen** Schlüssel plus die EBICS-Schlüsselversion (`StoredPublicKey`). INI legt
den Signaturschlüssel (`A00x`) ab; HIA (#27) nutzt denselben Store für Authentifikations-
(`X00x`) und Verschlüsselungsschlüssel (`E00x`). Das Domänen-Aggregat `Subscriber` bleibt
bewusst schlüsselfrei (siehe [Stammdaten](master-data.md)).

## Returncodes & Fehlerfälle

Wie beim gesamten `/ebics`-Endpoint werden Protokoll-/Businessfehler mit **HTTP 200** und
einem Returncode im Envelope beantwortet (siehe [host.md](host.md)); der fachliche Code
steht in `body/ReturnCode`.

| Situation | Returncode |
| --- | --- |
| INI angenommen | `000000` EBICS_OK |
| Teilnehmer unbekannt **oder** nicht mehr `New` (bereits initialisiert) | `091002` EBICS_INVALID_USER_OR_USER_STATE |
| Order-Data nicht entpack-/deserialisierbar, unbrauchbares/unzulässiges Schlüsselmaterial oder falsche Signaturversion | `090004` EBICS_INVALID_ORDER_DATA_FORMAT |

Re-INI wird also **strikt abgelehnt**, sobald der Teilnehmer nicht mehr `New` ist — das
deckt sich mit den erlaubten Übergängen der Domäne (`New → Initialized`).

### ⚠️ Spec-Vorbehalte

- Die konkreten Codes (`091002` für „bereits initialisiert", `090004` für Order-Data-Format)
  sind gegen den offiziellen EBICS-Annex 1 zu verifizieren; der vollständige, zentrale
  Returncode-Katalog kommt mit **#36 (M4)**.
- Die Antwort ist **unsigniert** — die Antwort-Authentifikationssignatur (X002) ist **M4**;
  strikte Clients könnten unsignierte Antworten ablehnen (konsistent mit `EbicsResponseFactory`).
- **H005:** aus dem übermittelten Zertifikat wird nur der öffentliche Schlüssel entnommen und
  gespeichert; eine Zertifikatsketten-/Selbstsignaturprüfung ist ein Conformance-Thema (**M8**).
- `OrderAttribute`/`SecurityMedium` werden nicht erzwungen (unverifiziert, wie im Connector).

## EBICS-Versionsbezug

| Version | Order-Data | Schlüsseltransport | OrderType-Feld |
| --- | --- | --- | --- |
| H003 / H004 | `S001.SignaturePubKeyOrderData` | `RSAKeyValue` (Modulus/Exponent) | `OrderType` |
| H005 | `S002.SignaturePubKeyOrderData` | `X509Data` (Zertifikat) | `AdminOrderType` |

Erlaubte Signaturversionen (via `KeyVersions`): **A004** (nur H003/H004), **A005** (alle),
**A006** (nur H005). Eine für die Protokollversion unzulässige Version (z. B. A006 auf H004)
wird mit `090004` abgelehnt.

## Tests

`tests/EBICO.Tests/Server/` (xUnit v3 + AwesomeAssertions; Request-XML aus committeten
Core-Bindings, keine proprietären Fixtures):

- `IniOrderHandlerTests` — End-to-End über `EbicsRequestPipeline`, `[Theory]` über H003/H004/H005:
  Happy Path (Antwort `ebicsKeyManagementResponse` `000000`, Teilnehmer `New→Initialized`,
  Schlüssel im `IServerKeyStore` mit passendem Modulus/Version) plus Negativfälle: bereits
  initialisiert und unbekannter Teilnehmer (`091002`), undekodierbare Order-Data (`090004`),
  für die Protokollversion unzulässige (A006/H004) bzw. zweckfremde (X002) Signaturversion (`090004`).
- `InMemoryServerKeyStoreTests` — Store/Get/Contains, Purpose-Isolation, Overwrite, Teilnehmer-Isolation.

## Verwandte Doku

- [Hostable Server-Grundgerüst](host.md) — Host, Pipeline, Returncodes, Response-Factory
- [Stammdatenverwaltung](master-data.md) — Teilnehmer-Lebenszyklus, `IMasterDataManager`, Store
- [Onboarding-Flows INI / HIA / HPB](../connector/onboarding.md) — der Client-Gegenpart
- [Schlüsselpaare & -repräsentation (A/E/X)](../protocol/key-representation.md) — Schlüsselversionen, RSAKeyValue/X.509-Import
- [Public-Key-Fingerprints (HPB/INI/HIA)](../protocol/public-key-fingerprint.md) — INI-Brief-Abgleich
